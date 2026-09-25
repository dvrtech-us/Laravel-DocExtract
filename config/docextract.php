<?php

declare(strict_types=1);

use Dvrtech\LaravelDocExtract\Version;

return [

    /*
    |--------------------------------------------------------------------------
    | Extractor binary
    |--------------------------------------------------------------------------
    |
    | Absolute path to DocExtract.exe. `php artisan docextract:install`
    | downloads the versioned Windows archive into this directory. Traineddata
    | ships inside that archive next to the executable (tessdata/eng.traineddata).
    | Point this at a .php script to run a test double through PHP_BINARY.
    |
    */

    'binary_path' => env(
        'DOCEXTRACT_BINARY_PATH',
        storage_path('docextract/bin/'.Version::VERSION.'/DocExtract.exe')
    ),

    /*
    |--------------------------------------------------------------------------
    | Private working directory
    |--------------------------------------------------------------------------
    |
    | Parent directory for the per-call temp folder. Null uses the system temp
    | directory. Each extract() call creates a private child directory and
    | removes it on cleanup().
    |
    */

    'temp_path' => env('DOCEXTRACT_TEMP_PATH'),

    /*
    |--------------------------------------------------------------------------
    | Process limits passed to the executable
    |--------------------------------------------------------------------------
    */

    'timeout_seconds' => (int) env('DOCEXTRACT_TIMEOUT_SECONDS', 100),
    'memory_mb' => (int) env('DOCEXTRACT_MEMORY_MB', 1024),
    'cpu_seconds' => (int) env('DOCEXTRACT_CPU_SECONDS', 90),
    'max_output_chars' => (int) env('DOCEXTRACT_MAX_OUTPUT_CHARS', 2_000_000),
    'max_depth' => (int) env('DOCEXTRACT_MAX_DEPTH', 3),
    'ocr' => env('DOCEXTRACT_OCR', 'auto'),
    'tables' => env('DOCEXTRACT_TABLES', 'auto'),

    /*
    |--------------------------------------------------------------------------
    | Public GitHub release used by docextract:install
    |--------------------------------------------------------------------------
    |
    | Downloads DocExtract-win-x64-<version>.zip and the matching .sha256 from
    | the public release. No token is sent.
    |
    */

    'release_version' => env('DOCEXTRACT_RELEASE_VERSION', Version::VERSION),
    'repository' => env('DOCEXTRACT_REPOSITORY', 'dvrtech-us/Laravel-DocExtract'),

];
