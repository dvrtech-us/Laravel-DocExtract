<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

use Dvrtech\LaravelDocExtract\Exceptions\ExtractionFailedException;

final class OutputFiles
{
    public static function resolve(string $root, string $relative): string
    {
        $relative = str_replace('\\', '/', $relative);
        if ($relative === '' || str_contains($relative, "\0") || str_contains($relative, ':')) {
            throw new ExtractionFailedException('E_UNSAFE_PATH', 1);
        }

        if (str_starts_with($relative, '/') || preg_match('/^[A-Za-z]:/', $relative) === 1) {
            throw new ExtractionFailedException('E_UNSAFE_PATH', 1);
        }

        $parts = explode('/', $relative);
        foreach ($parts as $part) {
            if ($part === '' || $part === '.' || $part === '..') {
                throw new ExtractionFailedException('E_UNSAFE_PATH', 1);
            }
        }

        $rootReal = realpath($root);
        if ($rootReal === false) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $full = $rootReal.DIRECTORY_SEPARATOR.implode(DIRECTORY_SEPARATOR, $parts);
        $real = realpath($full);
        $prefix = rtrim($rootReal, DIRECTORY_SEPARATOR).DIRECTORY_SEPARATOR;
        if ($real === false || ! is_file($real) || ! str_starts_with($real, $prefix)) {
            throw new ExtractionFailedException($real === false || ! is_file($real) ? 'E_FAILED' : 'E_UNSAFE_PATH', 1);
        }

        return $real;
    }

    public static function read(string $root, string $relative): string
    {
        $path = self::resolve($root, $relative);
        $bytes = file_get_contents($path);
        if ($bytes === false) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        return $bytes;
    }
}
