<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

final readonly class ExtractedImage
{
    public function __construct(
        public string $id,
        public int $partIndex,
        public string $file,
        public string $mime,
        public int $width,
        public int $height,
        public int $sizeBytes,
        public int $ocrChars,
        private string $outputDir,
        private TempDirectory $temp,
    ) {
    }

    public function bytes(): string
    {
        $this->temp->guard();

        return OutputFiles::read($this->outputDir, $this->file);
    }
}
