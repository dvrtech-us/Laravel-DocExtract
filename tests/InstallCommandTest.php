<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Tests;

use Dvrtech\LaravelDocExtract\DocExtract;
use Dvrtech\LaravelDocExtract\TempDirectory;
use Illuminate\Support\Facades\Http;
use ZipArchive;

final class InstallCommandTest extends TestCase
{
    protected function tearDown(): void
    {
        TempDirectory::remove(storage_path('docextract'));
        parent::tearDown();
    }

    public function test_install_rejects_a_sha256_mismatch_before_extracting(): void
    {
        $zip = $this->archive();
        $this->fakeRelease($zip, false);
        $extractor = \Mockery::mock(DocExtract::class);
        $extractor->shouldNotReceive('selfTest');
        $this->app->instance(DocExtract::class, $extractor);

        $this->artisan('docextract:install')
            ->expectsOutputToContain('sha256 mismatch')
            ->assertFailed();

        $this->assertFileDoesNotExist(storage_path('docextract/bin/1.0.0/DocExtract.exe'));
        Http::assertSent(fn ($request): bool => $request->url() === 'https://github.com/dvrtech-us/Laravel-DocExtract/releases/download/v1.0.0/DocExtract-win-x64-1.0.0.zip'
            && ! $request->hasHeader('Authorization'));
    }

    public function test_install_extracts_a_matching_archive_and_runs_self_test(): void
    {
        $zip = $this->archive();
        $this->fakeRelease($zip, true);
        $extractor = \Mockery::mock(DocExtract::class);
        $extractor->shouldReceive('selfTest')->once()->with(false)->andReturn(['passed' => true]);
        $this->app->instance(DocExtract::class, $extractor);

        $this->artisan('docextract:install', ['--release' => '2.0.0'])->assertSuccessful();

        $binary = storage_path('docextract/bin/2.0.0/DocExtract.exe');
        $this->assertFileExists($binary);
        $this->assertFileExists(storage_path('docextract/bin/2.0.0/tessdata/eng.traineddata'));
        $this->assertSame(realpath($binary), realpath(config('docextract.binary_path')));
        Http::assertSent(fn ($request): bool => str_contains(
            $request->url(),
            '/releases/download/v2.0.0/DocExtract-win-x64-2.0.0.zip'
        ));
    }

    public function test_install_refuses_to_replace_an_existing_directory_without_force(): void
    {
        $dest = storage_path('docextract/bin/1.0.0');
        mkdir($dest, 0755, true);
        file_put_contents($dest.DIRECTORY_SEPARATOR.'keep.txt', 'keep');

        $extractor = \Mockery::mock(DocExtract::class);
        $extractor->shouldNotReceive('selfTest');
        $this->app->instance(DocExtract::class, $extractor);
        Http::fake();

        $this->artisan('docextract:install')
            ->expectsOutputToContain('already installed')
            ->assertFailed();

        $this->assertSame('keep', file_get_contents($dest.DIRECTORY_SEPARATOR.'keep.txt'));
    }

    public function test_install_force_replaces_an_existing_directory(): void
    {
        $dest = storage_path('docextract/bin/1.0.0');
        mkdir($dest, 0755, true);
        file_put_contents($dest.DIRECTORY_SEPARATOR.'keep.txt', 'keep');

        $this->fakeRelease($this->archive(), true);
        $extractor = \Mockery::mock(DocExtract::class);
        $extractor->shouldReceive('selfTest')->once()->with(false)->andReturn(['passed' => true]);
        $this->app->instance(DocExtract::class, $extractor);

        $this->artisan('docextract:install', ['--force' => true])->assertSuccessful();

        $this->assertFileDoesNotExist($dest.DIRECTORY_SEPARATOR.'keep.txt');
        $this->assertFileExists($dest.DIRECTORY_SEPARATOR.'DocExtract.exe');
    }

    private function fakeRelease(string $zip, bool $match): void
    {
        Http::preventStrayRequests();
        Http::fake(function ($request) use ($zip, $match) {
            $url = $request->url();
            if (str_ends_with($url, '.sha256')) {
                $hash = $match ? strtoupper(hash('sha256', $zip)) : str_repeat('ab', 32);

                return Http::response($hash."  archive.zip\n", 200);
            }

            if (str_ends_with($url, '.zip')) {
                return Http::response($zip, 200);
            }

            return Http::response('missing', 404);
        });
    }

    private function archive(): string
    {
        $path = tempnam(sys_get_temp_dir(), 'dxzip-');
        $this->assertNotFalse($path);
        $zip = new ZipArchive();
        $opened = $zip->open($path, ZipArchive::CREATE | ZipArchive::OVERWRITE);
        $this->assertTrue($opened);
        $zip->addFromString('DocExtract.exe', "placeholder\n");
        $zip->addFromString('tessdata/eng.traineddata', 'trained');
        $zip->close();
        $bytes = file_get_contents($path);
        @unlink($path);
        $this->assertNotFalse($bytes);

        return $bytes;
    }
}
