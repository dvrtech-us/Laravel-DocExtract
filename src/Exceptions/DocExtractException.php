<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Exceptions;

use RuntimeException;

abstract class DocExtractException extends RuntimeException
{
    public function __construct(
        string $errorCode,
        public readonly int $exitCode,
    ) {
        parent::__construct($errorCode);
    }

    public function errorCode(): string
    {
        return $this->getMessage();
    }
}
