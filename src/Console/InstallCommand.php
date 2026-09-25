<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Console;

use Dvrtech\LaravelDocExtract\DocExtract;
use Dvrtech\LaravelDocExtract\Exceptions\DocExtractException;
use Dvrtech\LaravelDocExtract\TempDirectory;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Http;

final class InstallCommand extends Command
{
    protected $signature = 'docextract:install
        {--release= : Release version to download}
        {--force : Replace an existing install}';

    protected $description = 'Download a public DocExtract Windows release, verify sha256, and self-test it';

    public function handle(DocExtract $extractor): int
    {
        $version = $this->version();
        $repository = $this->repository();
        $dest = storage_path('docextract/bin/'.$version);
        $force = (bool) $this->option('force');

        if (is_dir($dest) && ! $force) {
            $this->error('DocExtract '.$version.' is already installed. Re-run with --force to replace it.');

            return self::FAILURE;
        }

        $asset = 'DocExtract-win-x64-'.$version.'.zip';
        $base = 'https://github.com/'.$repository.'/releases/download/v'.$version.'/'.$asset;
        $zip = $this->download($base);
        $digest = $this->parseSha256($this->download($base.'.sha256'));
        $actual = hash('sha256', $zip);
        if (! hash_equals($digest, $actual)) {
            $this->error('Release archive sha256 mismatch.');

            return self::FAILURE;
        }

        if (is_dir($dest)) {
            TempDirectory::remove($dest);
        }

        if (! mkdir($dest, 0755, true) && ! is_dir($dest)) {
            throw new \RuntimeException('Could not create the install directory.');
        }

        try {
            $this->extractZip($zip, $dest);
        } catch (\Throwable $e) {
            TempDirectory::remove($dest);
            throw $e;
        }

        $binary = $dest.DIRECTORY_SEPARATOR.'DocExtract.exe';
        config(['docextract.binary_path' => $binary]);
        $this->info('Installed '.$binary);

        try {
            $report = $extractor->selfTest(false);
        } catch (DocExtractException $e) {
            $this->error($e->errorCode());

            return self::FAILURE;
        }

        if (($report['passed'] ?? false) !== true) {
            $this->error('E_SELF_TEST');

            return self::FAILURE;
        }

        $this->info('Self-test passed.');

        return self::SUCCESS;
    }

    private function version(): string
    {
        $version = $this->option('release');
        if (! is_string($version) || $version === '') {
            $version = config('docextract.release_version');
        }

        if (! is_string($version) || preg_match('/^\d+\.\d+\.\d+$/', $version) !== 1) {
            throw new \RuntimeException('Release version must be a dotted numeric version.');
        }

        return $version;
    }

    private function repository(): string
    {
        $repository = config('docextract.repository');
        if (! is_string($repository) || preg_match('/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/', $repository) !== 1) {
            throw new \RuntimeException('Release repository is not a public owner/name.');
        }

        return $repository;
    }

    private function download(string $url): string
    {
        $response = Http::timeout(300)
            ->withHeaders(['Accept' => '*/*'])
            ->get($url);

        if (! $response->successful()) {
            throw new \RuntimeException('Release download failed with HTTP '.$response->status().'.');
        }

        return $response->body();
    }

    private function parseSha256(string $body): string
    {
        if (preg_match('/\b([a-fA-F0-9]{64})\b/', $body, $matches) !== 1) {
            throw new \RuntimeException('Release sha256 file is not a sha256 digest.');
        }

        return strtolower($matches[1]);
    }

    private function extractZip(string $bytes, string $dest): void
    {
        $tmp = tempnam(sys_get_temp_dir(), 'docextract-zip-');
        if ($tmp === false) {
            throw new \RuntimeException('Could not stage the release archive.');
        }

        try {
            if (file_put_contents($tmp, $bytes) === false) {
                throw new \RuntimeException('Could not stage the release archive.');
            }

            $zip = new \ZipArchive();
            if ($zip->open($tmp) !== true) {
                throw new \RuntimeException('Release archive could not be opened.');
            }

            for ($i = 0; $i < $zip->numFiles; $i++) {
                $name = $zip->getNameIndex($i);
                if (! is_string($name) || $this->unsafeZipName($name)) {
                    $zip->close();
                    throw new \RuntimeException('Release archive contains an unsafe path.');
                }
            }

            if (! $zip->extractTo($dest)) {
                $zip->close();
                throw new \RuntimeException('Release archive could not be extracted.');
            }

            $zip->close();
        } finally {
            @unlink($tmp);
        }

        $exe = $dest.DIRECTORY_SEPARATOR.'DocExtract.exe';
        if (! is_file($exe)) {
            throw new \RuntimeException('Release archive does not contain DocExtract.exe.');
        }

        if (PHP_OS_FAMILY !== 'Windows') {
            chmod($exe, 0755);
        }
    }

    private function unsafeZipName(string $name): bool
    {
        $name = str_replace('\\', '/', $name);
        if ($name === '' || str_contains($name, "\0") || str_contains($name, ':') || str_starts_with($name, '/')) {
            return true;
        }

        foreach (explode('/', $name) as $part) {
            if ($part === '..') {
                return true;
            }
        }

        return false;
    }
}
