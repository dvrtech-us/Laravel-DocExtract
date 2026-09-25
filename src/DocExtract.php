<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

use Dvrtech\LaravelDocExtract\Exceptions\CorruptFileException;
use Dvrtech\LaravelDocExtract\Exceptions\DocExtractException;
use Dvrtech\LaravelDocExtract\Exceptions\ExtractionFailedException;
use Dvrtech\LaravelDocExtract\Exceptions\ExtractionTimeoutException;
use Dvrtech\LaravelDocExtract\Exceptions\LimitExceededException;
use Dvrtech\LaravelDocExtract\Exceptions\UnsupportedFileException;
use Illuminate\Contracts\Config\Repository;
use Symfony\Component\Process\Exception\ProcessTimedOutException;
use Symfony\Component\Process\Process;

class DocExtract
{
    public function __construct(private readonly Repository $config)
    {
    }

    /**
     * @param  array<string, mixed>  $options
     */
    public function extract(string $path, ?string $originalName = null, array $options = []): ExtractionResult
    {
        $input = realpath($path);
        if ($input === false || ! is_file($input)) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $timeout = $this->intOption($options, 'timeout_seconds');
        $logicalName = $this->logicalName($input, $originalName);
        $limits = [
            '--file-name', $logicalName,
            '--max-output-chars', (string) $this->intOption($options, 'max_output_chars'),
            '--max-depth', (string) $this->intOption($options, 'max_depth', true),
            '--ocr', $this->choice($options, 'ocr'),
            '--tables', $this->choice($options, 'tables'),
            '--memory-mb', (string) $this->intOption($options, 'memory_mb'),
            '--cpu-seconds', (string) $this->intOption($options, 'cpu_seconds'),
            '--timeout-seconds', (string) $timeout,
        ];

        $root = $this->makeTempDirectory();
        try {
            $output = $root.DIRECTORY_SEPARATOR.'out';
            if (! mkdir($output, 0700) && ! is_dir($output)) {
                throw new ExtractionFailedException('E_FAILED', 1);
            }

            $args = ['extract', '--input', $input, '--output-dir', $output, ...$limits];
            $process = $this->process($args, $timeout, $root);
            $exit = $this->wait($process);
            if ($exit !== 0) {
                throw $this->exceptionFor($exit, $process->getErrorOutput());
            }

            return $this->parse($root, $output);
        } catch (\Throwable $e) {
            TempDirectory::remove($root);
            throw $e;
        }
    }

    /**
     * @return array<string, mixed>
     */
    public function selfTest(bool $requireOcr = false): array
    {
        $args = ['self-test'];
        if ($requireOcr) {
            $args[] = '--require-ocr';
        }

        return $this->jsonCommand($args, $this->intOption([], 'timeout_seconds'));
    }

    /**
     * @return array<string, mixed>
     */
    public function version(): array
    {
        return $this->jsonCommand(['version'], 30);
    }

    /**
     * @param  list<string>  $args
     * @return array<string, mixed>
     */
    private function jsonCommand(array $args, int $timeout): array
    {
        $root = $this->makeTempDirectory();
        try {
            $process = $this->process($args, $timeout, $root);
            $exit = $this->wait($process);
            if ($exit !== 0) {
                throw $this->exceptionFor($exit, $process->getErrorOutput());
            }

            $decoded = $this->decodeJson($this->jsonPayload($process->getOutput()));
            if (! is_array($decoded)) {
                throw new ExtractionFailedException('E_FAILED', 1);
            }

            return $decoded;
        } catch (\Throwable $e) {
            throw $e;
        } finally {
            TempDirectory::remove($root);
        }
    }

    /**
     * @param  list<string>  $args
     */
    private function process(array $args, int $timeout, string $cwd): Process
    {
        $binary = $this->binaryPath();
        $command = str_ends_with(strtolower($binary), '.php')
            ? [PHP_BINARY, $binary, ...$args]
            : [$binary, ...$args];

        $process = new Process($command, $cwd, $this->environment($cwd));
        $process->setTimeout((float) $timeout);

        return $process;
    }

    private function wait(Process $process): int
    {
        try {
            $process->run();
        } catch (ProcessTimedOutException) {
            throw new ExtractionTimeoutException('E_TIMEOUT', 5);
        } catch (\Throwable) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $exit = $process->getExitCode();

        return $exit ?? 1;
    }

    private function exceptionFor(int $exitCode, string $stderr): DocExtractException
    {
        $code = $this->errorCode($stderr, $exitCode);

        return match ($exitCode) {
            2 => new UnsupportedFileException($code, 2),
            3 => new LimitExceededException($code, 3),
            4 => new CorruptFileException($code, 4),
            5 => new ExtractionTimeoutException($code, 5),
            default => new ExtractionFailedException($code, $exitCode),
        };
    }

    private function errorCode(string $stderr, int $exitCode): string
    {
        $last = null;
        foreach (preg_split("/\r\n|\n|\r/", $stderr) ?: [] as $line) {
            $line = trim($line);
            if (preg_match('/^E_[A-Z_]+$/', $line) === 1) {
                $last = $line;
            }
        }

        if ($last !== null) {
            return $last;
        }

        return match ($exitCode) {
            2 => 'E_UNSUPPORTED',
            5 => 'E_TIMEOUT',
            default => 'E_FAILED',
        };
    }

    private function parse(string $root, string $output): ExtractionResult
    {
        $jsonPath = $output.DIRECTORY_SEPARATOR.'result.json';
        if (! is_file($jsonPath)) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $raw = file_get_contents($jsonPath);
        if ($raw === false) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $decoded = $this->decodeJson($raw);
        if (! is_array($decoded) || ! isset($decoded['parts'], $decoded['images']) || ! is_array($decoded['parts']) || ! is_array($decoded['images'])) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        foreach ($decoded['parts'] as $part) {
            if (! is_array($part) || ! isset($part['markdownFile']) || ! is_string($part['markdownFile'])) {
                throw new ExtractionFailedException('E_FAILED', 1);
            }
            OutputFiles::resolve($output, $part['markdownFile']);
        }

        foreach ($decoded['images'] as $image) {
            if (! is_array($image) || ! isset($image['file']) || ! is_string($image['file'])) {
                throw new ExtractionFailedException('E_FAILED', 1);
            }
            OutputFiles::resolve($output, $image['file']);
        }

        $temp = new TempDirectory($root);
        $parts = [];
        foreach ($decoded['parts'] as $part) {
            $warnings = [];
            foreach ($part['warnings'] ?? [] as $warning) {
                if (! is_array($warning)) {
                    continue;
                }
                $warnings[] = new Warning((string) ($warning['code'] ?? ''), (string) ($warning['detail'] ?? ''));
            }

            $imageIds = [];
            foreach ($part['imageIds'] ?? [] as $imageId) {
                $imageIds[] = (string) $imageId;
            }

            $parts[] = new ExtractedPart(
                index: (int) ($part['index'] ?? 0),
                path: (string) ($part['path'] ?? ''),
                depth: (int) ($part['depth'] ?? 0),
                kind: (string) ($part['kind'] ?? ''),
                mime: (string) ($part['mime'] ?? ''),
                sizeBytes: (int) ($part['sizeBytes'] ?? 0),
                pages: isset($part['pages']) && $part['pages'] !== null ? (int) $part['pages'] : null,
                engine: (string) ($part['engine'] ?? ''),
                durationMs: (int) ($part['durationMs'] ?? 0),
                charStart: (int) ($part['charStart'] ?? 0),
                charEnd: (int) ($part['charEnd'] ?? 0),
                markdownFile: (string) $part['markdownFile'],
                truncated: (bool) ($part['truncated'] ?? false),
                warnings: $warnings,
                imageIds: $imageIds,
                outputDir: $output,
                temp: $temp,
            );
        }

        $images = [];
        foreach ($decoded['images'] as $image) {
            $images[] = new ExtractedImage(
                id: (string) ($image['id'] ?? ''),
                partIndex: (int) ($image['partIndex'] ?? 0),
                file: (string) $image['file'],
                mime: (string) ($image['mime'] ?? ''),
                width: (int) ($image['width'] ?? 0),
                height: (int) ($image['height'] ?? 0),
                sizeBytes: (int) ($image['sizeBytes'] ?? 0),
                ocrChars: (int) ($image['ocrChars'] ?? 0),
                outputDir: $output,
                temp: $temp,
            );
        }

        return new ExtractionResult(
            schemaVersion: (int) ($decoded['schemaVersion'] ?? 0),
            extractorVersion: (string) ($decoded['extractorVersion'] ?? ''),
            status: (string) ($decoded['status'] ?? ''),
            totalChars: (int) ($decoded['totalChars'] ?? 0),
            truncated: (bool) ($decoded['truncated'] ?? false),
            parts: $parts,
            images: $images,
            temp: $temp,
        );
    }

    /**
     * @return mixed
     */
    private function decodeJson(string $json)
    {
        try {
            return json_decode($json, true, 512, JSON_THROW_ON_ERROR);
        } catch (\JsonException) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }
    }

    private function jsonPayload(string $stdout): string
    {
        $start = strpos($stdout, '{');
        $end = strrpos($stdout, '}');
        if ($start === false || $end === false || $end < $start) {
            return $stdout;
        }

        return substr($stdout, $start, $end - $start + 1);
    }

    private function binaryPath(): string
    {
        $configured = $this->config->get('docextract.binary_path');
        if (! is_string($configured) || $configured === '') {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $real = realpath($configured);
        if ($real === false || ! is_file($real)) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        return $real;
    }

    private function logicalName(string $path, ?string $originalName): string
    {
        $name = $originalName ?? '';
        if ($name === '') {
            $name = basename(str_replace('\\', '/', $path));
        }

        $name = str_replace('\\', '/', $name);
        $name = basename($name);
        $name = str_replace("\0", '', $name);
        if ($name === '' || $name === '.' || $name === '..') {
            $name = 'document';
        }

        if (str_starts_with($name, '-')) {
            $name = '_'.$name;
        }

        return $name;
    }

    /**
     * @param  array<string, mixed>  $options
     */
    private function intOption(array $options, string $key, bool $allowZero = false): int
    {
        $value = array_key_exists($key, $options) ? $options[$key] : $this->config->get('docextract.'.$key);
        if (is_string($value) && is_numeric($value)) {
            $value = $value + 0;
        }

        if (! is_int($value)) {
            throw new ExtractionFailedException('E_USAGE', 1);
        }

        if ($allowZero ? $value < 0 : $value <= 0) {
            throw new ExtractionFailedException('E_USAGE', 1);
        }

        return $value;
    }

    /**
     * @param  array<string, mixed>  $options
     */
    private function choice(array $options, string $key): string
    {
        $value = array_key_exists($key, $options) ? $options[$key] : $this->config->get('docextract.'.$key);
        if (! is_string($value) || ! in_array($value, ['auto', 'off'], true)) {
            throw new ExtractionFailedException('E_USAGE', 1);
        }

        return $value;
    }

    /**
     * @return array<string, string>
     */
    private function environment(string $root): array
    {
        $env = [];
        $inherited = getenv();
        if (is_array($inherited)) {
            foreach ($inherited as $key => $value) {
                if (is_string($key) && is_string($value)) {
                    $env[$key] = $value;
                }
            }
        }

        $env['TEMP'] = $root;
        $env['TMP'] = $root;
        $env['TMPDIR'] = $root;

        return $env;
    }

    private function makeTempDirectory(): string
    {
        $base = $this->config->get('docextract.temp_path');
        if (! is_string($base) || $base === '') {
            $base = sys_get_temp_dir();
        }

        if (! is_dir($base) && ! mkdir($base, 0700, true) && ! is_dir($base)) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $root = rtrim($base, DIRECTORY_SEPARATOR).DIRECTORY_SEPARATOR.'docextract-'.bin2hex(random_bytes(8));
        if (! mkdir($root, 0700) && ! is_dir($root)) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        return $root;
    }
}
