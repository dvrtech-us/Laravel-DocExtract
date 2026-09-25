<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

use Dvrtech\LaravelDocExtract\Exceptions\ExtractionFailedException;

final class TempDirectory
{
    private bool $deleted = false;

    public function __construct(public readonly string $path)
    {
    }

    public function guard(): void
    {
        if ($this->deleted) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }
    }

    public function delete(): void
    {
        if ($this->deleted) {
            return;
        }

        $this->deleted = true;
        self::remove($this->path);
    }

    public static function remove(string $path): void
    {
        if ($path === '' || ! is_dir($path)) {
            return;
        }

        $iterator = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($path, \FilesystemIterator::SKIP_DOTS),
            \RecursiveIteratorIterator::CHILD_FIRST
        );

        foreach ($iterator as $item) {
            if ($item->isDir()) {
                @rmdir($item->getPathname());
            } else {
                @unlink($item->getPathname());
            }
        }

        @rmdir($path);
    }
}
