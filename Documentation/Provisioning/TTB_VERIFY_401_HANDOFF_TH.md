# ข้อความประสานผล Verify UAT

ร่างสำหรับให้ผู้ใช้ส่งเอง ยังไม่ได้ส่งให้บุคคลภายนอก ไม่แนบ private key หรือ callback password ในข้อความนี้

สวัสดีครับคุณ @bird

ผมตั้งค่า Merchant PYA และ JWT iss เป็น jwt-key-pya แล้วครับ ได้ลองเรียก Verify Slip จาก ntserver02 ล่าสุดเมื่อวันที่ 20 ตุลาคม 2026 เวลา 14:22:54 น. แต่ยังได้รับ HTTP401 ครับ

Request UID: `41982f65-8fcd-4b8e-8f27-ea82f25080be`

Endpoint: `https://uat-dgw.tau2904.com:9643/qrapiservices/verify/verifyqr/PYA`

รบกวนช่วยตรวจให้หน่อยได้ไหมครับว่า public keyของบริษัทที่แนบ ได้ลงทะเบียนกับ issuerนี้แล้วหรือยัง และ gatewayติดเงื่อนไข authenticationส่วนใดครับ หาก QR APIของเราจำเป็นต้องส่ง sender_name รบกวนแจ้งค่าของบริษัทที่ต้องใช้ด้วยครับ

ผมใช้RS256/RSA2048 และheader typeเป็นJWTตามตัวอย่างที่แจ้งมาแล้วครับ แนบเฉพาะ [public keyของ UAT](ttb-uat-jwt-5183ee1873474f4ab9aff6f1b530d588-public.pem) โดย fingerprint SHA256ของDER-SPKIคือ `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`

ส่วน Credit Notification ผมเตรียม URL และ Basic authenticationไว้แล้วครับ รบกวนขอช่องทางส่วนตัวที่สะดวกให้ผมส่ง username/password และนัดสแกน QR1บาทในวงทดสอบธนาคารร่วมกันได้เลยครับ

ขอบคุณครับ
