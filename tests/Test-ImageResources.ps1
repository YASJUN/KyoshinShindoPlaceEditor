$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath = Join-Path $PSScriptRoot '..\KyoshinShindoPlaceEditor\MainForm.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8
$urls = [regex]::Matches($source, '"(http://www\.kmoni\.bosai\.go\.jp/[^"]+)"') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
if ($urls.Count -ne 3) { throw 'Expected one base map and two monitor image sources.' }

$stamp = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(9)).AddSeconds(-20).ToString('yyyyMMddHHmmss')
foreach ($baseUrl in $urls) {
    $url = $baseUrl
    if ($url -match '/(jma_[sb])/$') {
        $type = $Matches[1]
        $url += $stamp.Substring(0, 8) + '/' + $stamp + '.' + $type + '.gif'
    }
    $request = [System.Net.WebRequest]::Create($url)
    $request.Timeout = 15000
    $response = $null
    $image = $null
    $buffer = New-Object System.IO.MemoryStream
    try {
        $response = $request.GetResponse()
        $response.GetResponseStream().CopyTo($buffer)
        $buffer.Position = 0
        $image = [System.Drawing.Image]::FromStream($buffer)
        if ($image.Width -ne 352 -or $image.Height -ne 400) {
            throw "Unexpected image dimensions: $($image.Width)x$($image.Height)."
        }
        Write-Output "PASS 352x400 $url"
    }
    finally {
        if ($image) { $image.Dispose() }
        $buffer.Dispose()
        if ($response) { $response.Dispose() }
    }
}
