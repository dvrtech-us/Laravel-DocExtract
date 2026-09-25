<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Tests;

use Dvrtech\LaravelDocExtract\DocExtract;
use Dvrtech\LaravelDocExtract\Exceptions\CorruptFileException;
use Dvrtech\LaravelDocExtract\Exceptions\ExtractionFailedException;
use Dvrtech\LaravelDocExtract\Exceptions\ExtractionTimeoutException;
use Dvrtech\LaravelDocExtract\Exceptions\LimitExceededException;
use Dvrtech\LaravelDocExtract\Exceptions\UnsupportedFileException;

final class DocExtractTest extends TestCase
{
    public function test_parses_a_successful_result(): void
    {
        $source = $this->fixture('Hello');
        $result = $this->extractor()->extract($source, 'notes.txt');

        try {
            $this->assertSame(1, $result->schemaVersion);
            $this->assertSame('1.0.0', $result->extractorVersion);
            $this->assertSame('ok', $result->status);
            $this->assertFalse($result->truncated);
            $this->assertCount(1, $result->parts);
            $this->assertSame('notes.txt', $result->parts[0]->path);
            $this->assertSame('text', $result->parts[0]->kind);
            $this->assertNull($result->parts[0]->pages);
            $this->assertSame('W_OCR_USED', $result->parts[0]->warnings[0]->code);
            $this->assertSame('img-0', $result->parts[0]->warnings[0]->detail);
            $markdown = $result->parts[0]->markdown();
            $this->assertStringContainsString('## notes.txt', $markdown);
            $this->assertSame($markdown, $result->text());
            $this->assertSame($markdown, $result->page($result->parts[0]->charStart, $result->parts[0]->charEnd));
            $this->assertSame($result->parts[0]->charEnd, $result->totalChars);
            $this->assertCount(1, $result->images);
            $this->assertSame('image/png', $result->images[0]->mime);
            $this->assertStringStartsWith("\x89PNG", $result->images[0]->bytes());
            $this->assertStringStartsWith('ocr=auto;tables=auto;depth=3', $result->parts[0]->engine);
        } finally {
            $result->cleanup();
            @unlink($source);
        }
    }

    public function test_pages_concatenated_markdown_by_utf16_offset(): void
    {
        $source = $this->fixture("PAGE\n");
        $result = $this->extractor()->extract($source);

        try {
            $this->assertSame('Hello 😀', $result->parts[0]->markdown());
            $this->assertSame('Tail', $result->parts[1]->markdown());
            $this->assertSame(8, $result->parts[0]->charEnd);
            $this->assertSame(8, $result->parts[1]->charStart);
            $this->assertSame('Hello 😀Tail', $result->text());
            $this->assertSame('Hello', $result->page(0, 5));
            $this->assertSame('😀', $result->page(6, 8));
            $this->assertSame('', $result->page(6, 7));
            $this->assertTrue(mb_check_encoding($result->page(6, 7), 'UTF-8'));
            $this->assertSame('Tail', $result->page($result->parts[1]->charStart, $result->parts[1]->charEnd));
        } finally {
            $result->cleanup();
            @unlink($source);
        }
    }

    /**
     * @param  class-string<\Throwable>  $exception
     */
    #[\PHPUnit\Framework\Attributes\DataProvider('exitCodes')]
    public function test_maps_exit_codes_to_typed_exceptions(string $directive, string $exception, string $code, int $exit): void
    {
        $source = $this->fixture($directive."\n");

        try {
            $this->extractor()->extract($source);
            $this->fail('Expected '.$exception);
        } catch (\Throwable $e) {
            $this->assertInstanceOf($exception, $e);
            $this->assertSame($code, $e->getMessage());
            $this->assertStringNotContainsString('leaked', $e->getMessage());
            $this->assertSame($exit, $e->exitCode);
        } finally {
            @unlink($source);
        }
    }

    /**
     * @return array<string, array{0: string, 1: class-string, 2: string, 3: int}>
     */
    public static function exitCodes(): array
    {
        return [
            'unsupported' => ['FAIL 2 E_UNSUPPORTED', UnsupportedFileException::class, 'E_UNSUPPORTED', 2],
            'limit' => ['FAIL 3 E_LIMIT_INPUT_SIZE', LimitExceededException::class, 'E_LIMIT_INPUT_SIZE', 3],
            'corrupt' => ['FAIL 4 E_CORRUPT', CorruptFileException::class, 'E_CORRUPT', 4],
            'encrypted' => ['FAIL 4 E_ENCRYPTED', CorruptFileException::class, 'E_ENCRYPTED', 4],
            'timeout exit' => ['FAIL 5 E_TIMEOUT', ExtractionTimeoutException::class, 'E_TIMEOUT', 5],
            'failed' => ['FAIL 1 E_FAILED', ExtractionFailedException::class, 'E_FAILED', 1],
        ];
    }

    public function test_process_timeout_raises_timeout_and_removes_the_workspace(): void
    {
        $temp = sys_get_temp_dir().DIRECTORY_SEPARATOR.'docextract-phpunit-'.bin2hex(random_bytes(4));
        mkdir($temp);
        $this->app['config']->set('docextract.temp_path', $temp);
        $this->app['config']->set('docextract.timeout_seconds', 1);
        $source = $this->fixture("SLEEP 5\n");

        try {
            $this->extractor()->extract($source);
            $this->fail('Expected ExtractionTimeoutException');
        } catch (ExtractionTimeoutException $e) {
            $this->assertSame('E_TIMEOUT', $e->getMessage());
            $this->assertSame(5, $e->exitCode);
        } finally {
            @unlink($source);
        }

        $this->assertSame([], glob($temp.DIRECTORY_SEPARATOR.'docextract-*') ?: []);
        @rmdir($temp);
    }

    public function test_rejects_paths_that_leave_the_output_directory(): void
    {
        foreach (['UNSAFE markdown', 'UNSAFE image'] as $directive) {
            $source = $this->fixture($directive."\n");
            try {
                $this->extractor()->extract($source);
                $this->fail('Expected E_UNSAFE_PATH for '.$directive);
            } catch (ExtractionFailedException $e) {
                $this->assertSame('E_UNSAFE_PATH', $e->getMessage());
                $this->assertSame(1, $e->exitCode);
            } finally {
                @unlink($source);
            }
        }
    }

    public function test_prefixes_a_leading_dash_and_drops_directory_segments_from_the_file_name(): void
    {
        $source = $this->fixture('Hello');
        $extractor = $this->extractor();

        $dashed = $extractor->extract($source, '-notes.txt');
        $nested = $extractor->extract($source, '../../secret.txt');
        $flags = $extractor->extract($source, '--secret.txt');

        try {
            $this->assertSame('_-notes.txt', $dashed->parts[0]->path);
            $this->assertSame('secret.txt', $nested->parts[0]->path);
            $this->assertSame('_--secret.txt', $flags->parts[0]->path);
            $this->assertStringContainsString('## _-notes.txt', $dashed->parts[0]->markdown());
        } finally {
            $dashed->cleanup();
            $nested->cleanup();
            $flags->cleanup();
            @unlink($source);
        }
    }

    public function test_cleanup_is_idempotent_and_blocks_later_reads(): void
    {
        $source = $this->fixture('Hello');
        $result = $this->extractor()->extract($source);
        $workspace = $result->workspace();
        $this->assertDirectoryExists($workspace);
        $result->cleanup();
        $result->cleanup();
        $this->assertDirectoryDoesNotExist($workspace);

        try {
            $result->parts[0]->markdown();
            $this->fail('Expected a failure after cleanup');
        } catch (ExtractionFailedException $e) {
            $this->assertSame('E_FAILED', $e->getMessage());
        } finally {
            @unlink($source);
        }
    }

    public function test_missing_binary_fails_with_the_error_code_only(): void
    {
        $this->app['config']->set('docextract.binary_path', sys_get_temp_dir().'/missing-docextract.exe');
        $source = $this->fixture('Hello');

        try {
            $this->extractor()->extract($source);
            $this->fail('Expected ExtractionFailedException');
        } catch (ExtractionFailedException $e) {
            $this->assertSame('E_FAILED', $e->getMessage());
            $this->assertStringNotContainsString('missing-docextract', $e->getMessage());
        } finally {
            @unlink($source);
        }
    }

    public function test_version_and_self_test_helpers(): void
    {
        $version = $this->extractor()->version();
        $this->assertSame('1.0.0', $version['version']);

        $report = $this->extractor()->selfTest(false);
        $this->assertTrue($report['passed']);

        try {
            $this->extractor()->selfTest(true);
            $this->fail('Expected self-test failure');
        } catch (ExtractionFailedException $e) {
            $this->assertSame('E_SELF_TEST', $e->getMessage());
        }
    }

    public function test_self_test_command(): void
    {
        $this->artisan('docextract:self-test')
            ->expectsOutputToContain('"passed": true')
            ->assertSuccessful();

        $this->artisan('docextract:self-test', ['--require-ocr' => true])
            ->expectsOutputToContain('E_SELF_TEST')
            ->assertFailed();
    }

    public function test_passes_option_overrides_on_the_command_line(): void
    {
        $source = $this->fixture('Hello');
        $result = $this->extractor()->extract($source, null, [
            'ocr' => 'off',
            'tables' => 'off',
            'max_depth' => 1,
        ]);

        try {
            $this->assertSame('ocr=off;tables=off;depth=1', $result->parts[0]->engine);
        } finally {
            $result->cleanup();
            @unlink($source);
        }
    }

    private function extractor(): DocExtract
    {
        return $this->app->make(DocExtract::class);
    }

    private function fixture(string $contents): string
    {
        $path = tempnam(sys_get_temp_dir(), 'dxsrc-');
        $this->assertNotFalse($path);
        file_put_contents($path, $contents);

        return $path;
    }
}
