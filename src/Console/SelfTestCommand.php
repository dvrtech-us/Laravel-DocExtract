<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract\Console;

use Dvrtech\LaravelDocExtract\DocExtract;
use Dvrtech\LaravelDocExtract\Exceptions\DocExtractException;
use Illuminate\Console\Command;

final class SelfTestCommand extends Command
{
    protected $signature = 'docextract:self-test {--require-ocr : Fail when OCR is unavailable}';

    protected $description = 'Run the DocExtract executable self-test';

    public function handle(DocExtract $extractor): int
    {
        try {
            $report = $extractor->selfTest((bool) $this->option('require-ocr'));
        } catch (DocExtractException $e) {
            $this->error($e->errorCode());

            return self::FAILURE;
        }

        $encoded = json_encode($report, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);
        $this->line($encoded === false ? '{}' : $encoded);

        return ($report['passed'] ?? false) === true ? self::SUCCESS : self::FAILURE;
    }
}
