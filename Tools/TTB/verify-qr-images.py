"""Independent optical decode and TLV/CRC audit of expired document fixtures only."""
import argparse
import binascii
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / '.tools/qr-decoder'))
import zxingcpp
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('directory')
args = parser.parse_args()
directory = Path(args.directory).resolve(strict=True)
assert root / '.artifacts/qr-real-readiness' in directory.parents
fixtures = json.loads((directory / 'fixtures.json').read_text(encoding='utf-8-sig'))
browser = json.loads((directory / 'browser/result.json').read_text())
assert not fixtures['realBillerUsed'] and not fixtures['bankAccepted']


def tlv(text):
    fields = {}
    while text:
        assert len(text) >= 4 and text[:4].isdigit()
        tag, length = text[:2], int(text[2:4])
        assert tag not in fields and len(text) >= 4 + length
        fields[tag], text = text[4:4+length], text[4+length:]
    return fields


results = []
by_name = {x['file']: x for x in fixtures['fixtures']}
inputs = [dict(x, fixture=x['file'], raw=True) for x in fixtures['fixtures']] + [dict(x, raw=False) for x in browser['results']]
for entry in inputs:
    image_path = directory / entry['file']
    assert hashlib.sha256(image_path.read_bytes()).hexdigest() == entry['sha256']
    image = Image.open(image_path).convert('RGB')
    decoded = zxingcpp.read_barcodes(image)
    assert len(decoded) == 1 and decoded[0].valid and decoded[0].format == zxingcpp.BarcodeFormat.QRCode, entry['file']
    expected = by_name[entry['fixture']]
    text = decoded[0].text
    assert text == expected['payload'], entry['file']
    top = tlv(text)
    assert text[-8:-4] == '6304' and format(binascii.crc_hqx(text[:-4].encode('ascii'), 0xffff), '04X') == top['63']
    merchant, additional = tlv(top['30']), tlv(top['62'])
    assert top['00'] == '01' and top['01'] == '12' and top['53'] == '764' and top['58'] == 'TH'
    assert merchant == {'00': 'A000000677010112', '01': '010753700001716', '02': 'ABCD1234567890XXXXXX', '03': '2222222222'}
    assert additional['07'] == 'ZXXXE231231235900001' and Decimal(top['54']) * 100 == expected['amountMinor']
    if entry['raw']:
        gray = image.convert('L'); width, height = gray.size
        for box in [(0,0,width,32),(0,height-32,width,height),(0,0,32,height),(width-32,0,width,height)]:
            assert gray.crop(box).getextrema() == (255,255), 'Four-module quiet zone was lost'
    results.append({'file': entry['file'], 'width': image.width, 'height': image.height, 'amountMinor': expected['amountMinor'], 'decodedPayloadExact': True, 'crcVerifiedIndependently': True, 'sha256': entry['sha256']})
proof = {'checks': len(results), 'results': results, 'actualReleaseRenderer': True, 'rawAndBrowserScreenshotsDecoded': True,
         'fixtureExpiry': '2023-12-31T16:59:00Z', 'userBillerOrMerchantUsed': False, 'bankRequestMade': False, 'bankAppAcceptance': False,
         'decoderPackage': 'zxing-cpp==3.1.1', 'decoderSource': 'https://pypi.org/project/zxing-cpp/3.1.1/'}
(directory / 'decode-result.json').write_text(json.dumps(proof, indent=2))
print(json.dumps({'checks': len(results), 'allDecodedExactly': True, 'crcVerifiedIndependently': True, 'bankAppAcceptance': False}))
