# Keep UTF-8 BOM for Windows PowerShell 5.1 to read Thai messages correctly.
param([ValidateRange(1024,65535)][int]$Port = 18765)
$ErrorActionPreference='Stop'
$url="http://127.0.0.1:$Port/internal/bank-test-console/"
Write-Host "เปิด URL นี้ในเบราว์เซอร์: $url"
Write-Host 'เปิดหน้าต่างนี้ค้างไว้ระหว่างใช้งาน กด Ctrl+C เพื่อปิด tunnel'
Write-Host 'คอนโซลใช้สิทธิ์ SSH เดิมของคุณ และเปิดเฉพาะเครื่องคุณที่ 127.0.0.1'
& ssh -o BatchMode=yes -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 `
    -L "127.0.0.1:${Port}:127.0.0.1:${Port}" ntserver02 `
    "kubectl -n ttb-uat port-forward service/ttb-commerce-api ${Port}:8080 --address=127.0.0.1"
if($LASTEXITCODE -ne 0){throw 'Tunnel หยุดทำงาน ตรวจสอบ SSH หรือใช้ -Port หมายเลขอื่น แล้วเปิดใหม่'}
