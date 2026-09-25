<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

final readonly class Warning
{
    public function __construct(
        public string $code,
        public string $detail,
    ) {
    }
}
