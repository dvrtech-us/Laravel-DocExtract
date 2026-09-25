<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

final readonly class ExtractedPart
{
    /**
     * @param  list<Warning>  $warnings
     * @param  list<string>  $imageIds
     */
    public function __construct(
        public int $index,
        public string $path,
        public int $depth,
        public string $kind,
        public string $mime,
        public int $sizeBytes,
        public ?int $pages,
        public string $engine,
        public int $durationMs,
        public int $charStart,
        public int $charEnd,
        public string $markdownFile,
        public bool $truncated,
        public array $warnings,
        public array $imageIds,
        private string $outputDir,
        private TempDirectory $temp,
    ) {
    }

    public function markdown(): string
    {
        $this->temp->guard();

        return OutputFiles::read($this->outputDir, $this->markdownFile);
    }
}
