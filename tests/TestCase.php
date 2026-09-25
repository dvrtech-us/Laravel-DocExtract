<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Tests;

use Orchestra\Testbench\TestCase as Orchestra;

abstract class TestCase extends Orchestra
{
    protected function getPackageProviders($app): array
    {
        return [\Dvrtech\LaravelDocExtract\DocExtractServiceProvider::class];
    }

    protected function defineEnvironment($app): void
    {
        $fake = realpath(__DIR__.'/Fakes/fake-extractor.php');
        $app['config']->set('docextract.binary_path', $fake);
        $app['config']->set('docextract.temp_path', sys_get_temp_dir());
        $app['config']->set('docextract.timeout_seconds', 10);
        $app['config']->set('docextract.release_version', '1.0.0');
        $app['config']->set('docextract.repository', 'dvrtech-us/Laravel-DocExtract');
    }
}
