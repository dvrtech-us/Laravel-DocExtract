<?php

declare(strict_types=1);

namespace Dvrtech\LaravelDocExtract;

use Dvrtech\LaravelDocExtract\Exceptions\ExtractionFailedException;

final readonly class ExtractionResult
{
    /**
     * @param  list<ExtractedPart>  $parts
     * @param  list<ExtractedImage>  $images
     */
    public function __construct(
        public int $schemaVersion,
        public string $extractorVersion,
        public string $status,
        public int $totalChars,
        public bool $truncated,
        public array $parts,
        public array $images,
        private TempDirectory $temp,
    ) {
    }

    public function text(): string
    {
        $this->temp->guard();
        $text = '';
        foreach ($this->parts as $part) {
            $text .= $part->markdown();
        }

        return $text;
    }

    /**
     * Return the concatenated Markdown between two UTF-16 code unit offsets.
     * $charEnd is exclusive and matches result.json charStart/charEnd.
     */
    public function page(int $charStart, int $charEnd): string
    {
        if ($charStart < 0 || $charEnd < $charStart) {
            throw new \InvalidArgumentException('UTF-16 offset range is invalid.');
        }

        $encoded = mb_convert_encoding($this->text(), 'UTF-16LE', 'UTF-8');
        if ($encoded === false) {
            throw new ExtractionFailedException('E_FAILED', 1);
        }

        $start = $charStart * 2;
        $length = ($charEnd - $charStart) * 2;
        if ($start >= strlen($encoded) || $length === 0) {
            return '';
        }

        $slice = substr($encoded, $start, $length);
        if ($slice === false || $slice === '') {
            return '';
        }

        if ((strlen($slice) % 2) === 1) {
            $slice = substr($slice, 0, -1);
        }

        if (strlen($slice) >= 2) {
            $first = unpack('v', substr($slice, 0, 2));
            if (is_array($first) && $first[1] >= 0xDC00 && $first[1] <= 0xDFFF) {
                $slice = substr($slice, 2);
            }
        }

        if (strlen($slice) >= 2) {
            $last = unpack('v', substr($slice, -2));
            if (is_array($last) && $last[1] >= 0xD800 && $last[1] <= 0xDBFF) {
                $slice = substr($slice, 0, -2);
            }
        }

        if ($slice === '') {
            return '';
        }

        $utf8 = mb_convert_encoding($slice, 'UTF-8', 'UTF-16LE');

        return $utf8 === false ? '' : $utf8;
    }

    public function workspace(): string
    {
        return $this->temp->path;
    }

    public function cleanup(): void
    {
        $this->temp->delete();
    }

    public function __destruct()
    {
        $this->cleanup();
    }
}
