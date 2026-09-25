<?php

declare(strict_types=1);

/**
 * Test double for DocExtract.exe. The package runs it with PHP_BINARY when
 * binary_path ends in .php. Directives are the first line of --input.
 */

$args = array_slice($argv, 1);
$command = $args[0] ?? '';

if ($command === 'version') {
    echo json_encode(['version' => '1.0.0', 'engines' => ['text' => 'DocExtract']], JSON_UNESCAPED_SLASHES);
    exit(0);
}

if ($command === 'self-test') {
    $requireOcr = in_array('--require-ocr', $args, true);
    if ($requireOcr && getenv('DOCEXTRACT_FAKE_OCR') !== '1') {
        fwrite(STDERR, "E_SELF_TEST\n");
        exit(1);
    }

    echo json_encode(['passed' => true, 'ocrAvailable' => false], JSON_UNESCAPED_SLASHES);
    exit(0);
}

if ($command !== 'extract') {
    fwrite(STDERR, "E_USAGE\n");
    exit(1);
}

$opts = [];
for ($i = 1; $i < count($args); $i++) {
    $flag = $args[$i];
    if (! str_starts_with($flag, '--')) {
        fwrite(STDERR, "E_USAGE\n");
        exit(1);
    }

    $key = substr($flag, 2);
    $value = $args[$i + 1] ?? null;
    if ($value === null || str_starts_with($value, '--')) {
        fwrite(STDERR, "E_USAGE\n");
        exit(1);
    }

    $opts[$key] = $value;
    $i++;
}

foreach (['input', 'output-dir', 'file-name'] as $required) {
    if (! isset($opts[$required]) || $opts[$required] === '') {
        fwrite(STDERR, "E_USAGE\n");
        exit(1);
    }
}

$input = $opts['input'];
$absolute = str_starts_with($input, '/')
    || str_starts_with($input, '\\\\')
    || preg_match('/^[A-Za-z]:[\\\\\\/]/', $input) === 1;
if (! $absolute || ! is_file($input)) {
    fwrite(STDERR, "E_FAILED\n");
    exit(1);
}

$temp = getenv('TEMP');
if ($temp === false || $temp === '' || ! is_dir($temp)) {
    fwrite(STDERR, "E_FAILED\n");
    exit(1);
}

$raw = (string) file_get_contents($input);
$first = strtok($raw, "\r\n");
$first = $first === false ? '' : $first;

if (str_starts_with($first, 'FAIL ')) {
    $pieces = explode(' ', $first);
    $exit = (int) ($pieces[1] ?? 1);
    $code = $pieces[2] ?? 'E_FAILED';
    fwrite(STDERR, "leaked document text\n");
    fwrite(STDERR, $code."\n");
    exit($exit);
}

if (str_starts_with($first, 'SLEEP ')) {
    $seconds = (int) substr($first, 6);
    sleep(max(1, $seconds));
}

$output = $opts['output-dir'];
if (! is_dir($output) && ! mkdir($output, 0700, true) && ! is_dir($output)) {
    fwrite(STDERR, "E_FAILED\n");
    exit(1);
}

$png = base64_decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO3f1aMAAAAASUVORK5CYII=', true);
if ($png === false) {
    fwrite(STDERR, "E_FAILED\n");
    exit(1);
}

$fileName = $opts['file-name'];
$engine = 'ocr='.$opts['ocr'].';tables='.$opts['tables'].';depth='.$opts['max-depth'];

$markdowns = [];
$imageFile = 'images/img-0.png';
$unsafeImage = false;

if ($first === 'UNSAFE markdown') {
    $markdowns = [['file' => '../secret.md', 'body' => 'nope']];
} elseif ($first === 'UNSAFE image') {
    $markdowns = [['file' => 'parts/0.md', 'body' => "safe\n"]];
    $unsafeImage = true;
    $imageFile = '../escaped.png';
} elseif ($first === 'PAGE') {
    $markdowns = [
        ['file' => 'parts/0.md', 'body' => "Hello 😀"],
        ['file' => 'parts/1.md', 'body' => 'Tail'],
    ];
} else {
    $markdowns = [[
        'file' => 'parts/0.md',
        'body' => "## {$fileName}\n\nHello from the host application\n",
    ]];
}

$utf16Length = static function (string $text): int {
    $encoded = mb_convert_encoding($text, 'UTF-16LE', 'UTF-8');

    return intdiv(strlen($encoded), 2);
};

if (! $unsafeImage && $first !== 'UNSAFE markdown') {
    $imageDir = $output.DIRECTORY_SEPARATOR.'images';
    if (! is_dir($imageDir)) {
        mkdir($imageDir, 0700, true);
    }
    file_put_contents($output.DIRECTORY_SEPARATOR.str_replace('/', DIRECTORY_SEPARATOR, 'images/img-0.png'), $png);
}

$parts = [];
$offset = 0;
foreach ($markdowns as $index => $markdown) {
    if (! str_contains($markdown['file'], '..')) {
        $full = $output.DIRECTORY_SEPARATOR.str_replace('/', DIRECTORY_SEPARATOR, $markdown['file']);
        $dir = dirname($full);
        if (! is_dir($dir)) {
            mkdir($dir, 0700, true);
        }
        file_put_contents($full, $markdown['body']);
    }

    $length = $utf16Length($markdown['body']);
    $parts[] = [
        'index' => $index,
        'path' => $fileName,
        'depth' => 0,
        'kind' => 'text',
        'mime' => 'text/plain',
        'sizeBytes' => strlen($markdown['body']),
        'pages' => null,
        'engine' => $engine,
        'durationMs' => 1,
        'charStart' => $offset,
        'charEnd' => $offset + $length,
        'markdownFile' => $markdown['file'],
        'truncated' => false,
        'warnings' => $index === 0 ? [['code' => 'W_OCR_USED', 'detail' => 'img-0']] : [],
        'imageIds' => $index === 0 ? ['img-0'] : [],
    ];
    $offset += $length;
}

$document = [
    'schemaVersion' => 1,
    'extractorVersion' => '1.0.0',
    'status' => 'ok',
    'totalChars' => $offset,
    'truncated' => false,
    'parts' => $parts,
    'images' => [[
        'id' => 'img-0',
        'partIndex' => 0,
        'file' => $imageFile,
        'mime' => 'image/png',
        'width' => 1,
        'height' => 1,
        'sizeBytes' => strlen($png),
        'ocrChars' => 4,
    ]],
];

file_put_contents(
    $output.DIRECTORY_SEPARATOR.'result.json',
    json_encode($document, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE)
);

exit(0);
