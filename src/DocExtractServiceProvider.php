<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

use Dvrtech\LaravelDocExtract\Console\InstallCommand;
use Dvrtech\LaravelDocExtract\Console\SelfTestCommand;
use Illuminate\Support\ServiceProvider;

final class DocExtractServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        $this->mergeConfigFrom(__DIR__.'/../config/docextract.php', 'docextract');

        $this->app->singleton(DocExtract::class, function ($app): DocExtract {
            return new DocExtract($app['config']);
        });
    }

    public function boot(): void
    {
        if ($this->app->runningInConsole()) {
            $this->publishes([
                __DIR__.'/../config/docextract.php' => config_path('docextract.php'),
            ], 'docextract-config');

            $this->commands([
                InstallCommand::class,
                SelfTestCommand::class,
            ]);
        }
    }
}
