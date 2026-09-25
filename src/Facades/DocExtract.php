<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Facades;

use Illuminate\Support\Facades\Facade;

/**
 * @method static \Dvrtech\LaravelDocExtract\ExtractionResult extract(string $path, ?string $originalName = null, array $options = [])
 * @method static array selfTest(bool $requireOcr = false)
 * @method static array version()
 *
 * @see \Dvrtech\LaravelDocExtract\DocExtract
 */
final class DocExtract extends Facade
{
    protected static function getFacadeAccessor(): string
    {
        return \Dvrtech\LaravelDocExtract\DocExtract::class;
    }
}
