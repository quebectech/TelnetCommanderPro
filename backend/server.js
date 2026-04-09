const express = require('express');
const axios = require('axios');
const crypto = require('crypto');
const app = express();
app.use(express.json());

// ── Config ───────────────────────────────────────────────────────────────────
const CONSUMER_KEY    = process.env.CONSUMER_KEY;
const CONSUMER_SECRET = process.env.CONSUMER_SECRET;
const SHORTCODE       = process.env.SHORTCODE || '4167789';
const JSONBIN_API_KEY = process.env.JSONBIN_API_KEY;
const WALLET_BIN_ID   = process.env.WALLET_BIN_ID   || '69d61f3736566621a88dd80e';
const TOKENS_BIN_ID   = process.env.TOKENS_BIN_ID   || '69d667edaaba882197d84570';
const JSONBIN_URL     = 'https://api.jsonbin.io/v3/b';
const BASE_URL        = process.env.CALLBACK_URL?.replace('/mpesa/callback','') || 'https://telnetcommanderpro.onrender.com';

// ── JSONBin helpers ──────────────────────────────────────────────────────────
const jbHeaders = { 'X-Master-Key': JSONBIN_API_KEY, 'Content-Type': 'application/json' };

async function getTokens() {
    const res = await axios.get(`${JSONBIN_URL}/${TOKENS_BIN_ID}/latest`, { headers: jbHeaders });
    return res.data.record.tokens || [];
}
async function saveTokens(tokens) {
    await axios.put(`${JSONBIN_URL}/${TOKENS_BIN_ID}`, { tokens }, { headers: jbHeaders });
}
async function getWallets() {
    const res = await axios.get(`${JSONBIN_URL}/${WALLET_BIN_ID}/latest`, { headers: jbHeaders });
    return res.data.record.wallets || [];
}
async function saveWallets(wallets) {
    await axios.put(`${JSONBIN_URL}/${WALLET_BIN_ID}`, { wallets }, { headers: jbHeaders });
}

function generateToken() {
    return 'TCP-' + crypto.randomBytes(3).toString('hex').toUpperCase();
}

async function getMpesaToken() {
    const auth = Buffer.from(`${CONSUMER_KEY}:${CONSUMER_SECRET}`).toString('base64');
    const res = await axios.get(
        'https://api.safaricom.co.ke/oauth/v1/generate?grant_type=client_credentials',
        { headers: { Authorization: `Basic ${auth}` } }
    );
    return res.data.access_token;
}

// ── Routes ───────────────────────────────────────────────────────────────────
app.get('/', (req, res) => res.json({ status: 'TelnetCommanderPro backend running v2.0.3' }));

// Admin panel
app.get('/admin', (req, res) => {
    res.send(`<!DOCTYPE html>
<html>
<head>
<title>TCP Admin - Credit Payments</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  body { font-family: Arial, sans-serif; max-width: 500px; margin: 40px auto; padding: 20px; background: #f5f5f5; }
  h2 { color: #0078D4; }
  input, button { width: 100%; padding: 12px; margin: 8px 0; border-radius: 6px; border: 1px solid #ccc; font-size: 15px; box-sizing: border-box; }
  button { background: #0078D4; color: white; border: none; cursor: pointer; font-weight: bold; }
  button:hover { background: #005a9e; }
  #result { margin-top: 16px; padding: 12px; border-radius: 6px; display: none; }
  .success { background: #d4edda; color: #155724; }
  .error { background: #f8d7da; color: #721c24; }
  .pending { background: #fff3cd; color: #856404; margin-top: 10px; padding: 10px; border-radius: 6px; }
</style>
</head>
<body>
<h2>💰 TCP Admin - Credit Payment</h2>
<p>Enter the details from the customer's WhatsApp message to credit their wallet.</p>
<label>Token (e.g. TCP-82234F)</label>
<input type="text" id="token" placeholder="TCP-XXXXXX" style="text-transform:uppercase"/>
<label>Amount (KES)</label>
<input type="number" id="amount" placeholder="100" value="100"/>
<label>Hardware ID</label>
<input type="text" id="hwid" placeholder="Customer hardware ID"/>
<button onclick="credit()">✅ Credit Wallet</button>
<div id="result"></div>

<hr style="margin:30px 0"/>
<h3>📋 Pending Tokens</h3>
<button onclick="loadTokens()" style="background:#6c757d">Refresh Pending Tokens</button>
<div id="tokens"></div>

<script>
async function credit() {
  const res = document.getElementById('result');
  res.style.display = 'none';
  try {
    const r = await fetch('/admin/credit', {
      method: 'POST',
      headers: {'Content-Type':'application/json'},
      body: JSON.stringify({
        token: document.getElementById('token').value.toUpperCase(),
        amount: parseFloat(document.getElementById('amount').value),
        hardwareId: document.getElementById('hwid').value
      })
    });
    const data = await r.json();
    res.style.display = 'block';
    if (data.success) {
      res.className = 'success';
      res.innerHTML = '✅ ' + data.message;
    } else {
      res.className = 'error';
      res.innerHTML = '❌ ' + (data.error || 'Failed');
    }
  } catch(e) {
    res.style.display = 'block';
    res.className = 'error';
    res.innerHTML = '❌ ' + e.message;
  }
}

async function loadTokens() {
  const div = document.getElementById('tokens');
  div.innerHTML = 'Loading...';
  try {
    const r = await fetch('/admin/tokens');
    const data = await r.json();
    if (!data.tokens || data.tokens.length === 0) {
      div.innerHTML = '<p>No pending tokens.</p>';
      return;
    }
    div.innerHTML = data.tokens.map(t =>
      '<div class="pending"><b>' + t.token + '</b> | ' + t.hardwareId.substring(0,12) + '... | ' +
      (t.paid ? '✅ PAID KES ' + t.amount : (t.pendingReceipt ? '🧾 Receipt: <b>' + t.pendingReceipt + '</b>' : '⏳ Waiting')) +
      ' | ' + new Date(t.createdAt).toLocaleTimeString() + '</div>'
    ).join('');
  } catch(e) { div.innerHTML = 'Error: ' + e.message; }
}
</script>
</body>
</html>`);
});

// GET /admin/tokens
app.get('/admin/tokens', async (req, res) => {
    try {
        const tokens = await getTokens();
        const cutoff = Date.now() - 30 * 60 * 1000;
        res.json({ tokens: tokens.filter(t => t.createdAt > cutoff) });
    } catch (err) { res.status(500).json({ error: err.message }); }
});

// GET wallet balance
app.get('/wallet/:hardwareId', async (req, res) => {
    try {
        const wallets = await getWallets();
        const wallet = wallets.find(w => w.hardwareId === req.params.hardwareId);
        res.json(wallet || { hardwareId: req.params.hardwareId, balanceKes: 0, transactions: [] });
    } catch (err) { res.status(500).json({ error: err.message }); }
});

// POST /generate-token
app.post('/generate-token', async (req, res) => {
    const { hardwareId } = req.body;
    if (!hardwareId) return res.status(400).json({ error: 'hardwareId required' });

    try {
        const token = generateToken();
        const tokens = await getTokens();

        // Remove expired tokens (older than 30 min) and old tokens for this device
        const cutoff = Date.now() - 30 * 60 * 1000;
        const fresh = tokens.filter(t => t.createdAt > cutoff && t.hardwareId !== hardwareId);

        fresh.push({ token, hardwareId, createdAt: Date.now(), paid: false, amount: 0, mpesaRef: '' });
        await saveTokens(fresh);

        res.json({ token, paybill: SHORTCODE, instructions: `Send M-Pesa to Paybill ${SHORTCODE}, Account: ${token}` });
    } catch (err) { res.status(500).json({ error: err.message }); }
});

// POST /verify-transaction - verify by transaction code only (amount pre-filled by app)
app.post('/verify-transaction', async (req, res) => {
    const { transactionCode, token, hardwareId, amount } = req.body;
    if (!transactionCode || !token || !hardwareId) {
        return res.status(400).json({ error: 'transactionCode, token and hardwareId required' });
    }

    // Validate transaction code format
    const code = transactionCode.toUpperCase().replace(/O/g, '0').trim();
    if (!/^[A-Z0-9]{8,12}$/.test(code)) {
        return res.status(400).json({ error: 'Invalid transaction code format. Should be like UD9QB03GS7' });
    }

    try {
        const tokens = await getTokens();
        const entry = tokens.find(t => t.token === token.toUpperCase() && t.hardwareId === hardwareId);
        if (!entry) return res.status(404).json({ error: 'Token not found or expired. Generate a new token.' });
        if (entry.paid) return res.json({ success: true, alreadyCredited: true });

        // Check transaction code hasn't been used before (anti-fraud)
        const wallets = await getWallets();
        const alreadyUsed = wallets.some(w => w.transactions?.some(t => t.mpesaRef === code));
        if (alreadyUsed) {
            return res.status(400).json({ error: 'This transaction code has already been used.' });
        }

        const creditAmount = parseFloat(amount) || entry.amount || 100;

        // Credit wallet
        entry.paid = true;
        entry.amount = creditAmount;
        entry.mpesaRef = code;
        await saveTokens(tokens);

        let wallet = wallets.find(w => w.hardwareId === hardwareId);
        if (!wallet) { wallet = { hardwareId, balanceKes: 0, transactions: [] }; wallets.push(wallet); }

        wallet.balanceKes = (wallet.balanceKes || 0) + creditAmount;
        wallet.lastTopUp = new Date().toISOString();
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({ type: 'topup', amount: creditAmount, mpesaRef: code, token, date: new Date().toISOString() });
        await saveWallets(wallets);
        await saveTokens(tokens.filter(t => t.token !== token.toUpperCase()));

        console.log(`Transaction verified: ${code}, KES ${creditAmount} for ${hardwareId.substring(0,8)}`);
        res.json({ success: true, newBalance: wallet.balanceKes, amount: creditAmount, receipt: code });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// POST /verify-sms - parse M-Pesa SMS and auto-credit if token matches
app.post('/verify-sms', async (req, res) => {
    const { smsText, token, hardwareId } = req.body;
    if (!smsText || !token || !hardwareId) {
        return res.status(400).json({ error: 'smsText, token and hardwareId required' });
    }

    try {
        const tokens = await getTokens();
        const entry = tokens.find(t => t.token === token.toUpperCase() && t.hardwareId === hardwareId);
        if (!entry) return res.status(404).json({ error: 'Token not found or expired. Generate a new token.' });
        if (entry.paid) {
            return res.json({ success: true, alreadyCredited: true, message: 'Already credited' });
        }

        // Parse M-Pesa SMS
        // Format: "ABC123XYZ Confirmed. KshXXX.XX sent to BUSINESS NAME for account TCP-XXXXXX on DD/MM/YY..."
        const sms = smsText.trim();

        // Extract receipt number (first word, alphanumeric)
        const receiptMatch = sms.match(/^([A-Z0-9]{8,12})\s+Confirmed/i);
        if (!receiptMatch) {
            return res.status(400).json({ error: 'Invalid M-Pesa SMS format. Please paste the full confirmation message.' });
        }
        const receipt = receiptMatch[1].toUpperCase();

        // Extract amount
        const amountMatch = sms.match(/Ksh\s*([\d,]+\.?\d*)/i);
        if (!amountMatch) {
            return res.status(400).json({ error: 'Could not read amount from SMS.' });
        }
        const amount = parseFloat(amountMatch[1].replace(',', ''));

        // Extract account reference - must contain our token
        const accountMatch = sms.match(/for account\s+([A-Z0-9\-]+)/i);
        const accountRef = accountMatch ? accountMatch[1].toUpperCase() : '';

        console.log(`SMS parse: receipt=${receipt}, amount=${amount}, account=${accountRef}, token=${token}`);

        // Verify the account reference matches the token
        if (!accountRef.includes(token.toUpperCase())) {
            return res.status(400).json({ 
                error: `SMS account reference "${accountRef}" does not match token "${token}". Make sure you used the correct account number when paying.` 
            });
        }

        // Verify payment was to our shortcode
        if (!sms.toLowerCase().includes('amortech') && !sms.includes(SHORTCODE)) {
            return res.status(400).json({ error: 'SMS does not appear to be a payment to the correct Paybill.' });
        }

        // All checks passed - credit the wallet
        entry.paid = true;
        entry.amount = amount;
        entry.mpesaRef = receipt;
        await saveTokens(tokens);

        // Credit wallet
        const wallets = await getWallets();
        let wallet = wallets.find(w => w.hardwareId === hardwareId);
        if (!wallet) { wallet = { hardwareId, balanceKes: 0, transactions: [] }; wallets.push(wallet); }

        wallet.balanceKes = (wallet.balanceKes || 0) + amount;
        wallet.lastTopUp = new Date().toISOString();
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({ type: 'topup', amount, mpesaRef: receipt, token, date: new Date().toISOString() });
        await saveWallets(wallets);

        // Remove used token
        await saveTokens(tokens.filter(t => t.token !== token.toUpperCase()));

        console.log(`SMS verified and credited: ${receipt}, KES ${amount} for ${hardwareId.substring(0,8)}`);
        res.json({ success: true, newBalance: wallet.balanceKes, amount, receipt });

    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// POST /verify-receipt - user provides M-Pesa receipt number to auto-verify payment
app.post('/verify-receipt', async (req, res) => {
    const { receiptNumber, token, hardwareId } = req.body;
    if (!receiptNumber || !token || !hardwareId) {
        return res.status(400).json({ error: 'receiptNumber, token and hardwareId required' });
    }

    try {
        const tokens = await getTokens();
        const entry = tokens.find(t => t.token === token.toUpperCase() && t.hardwareId === hardwareId);
        if (!entry) return res.status(404).json({ error: 'Token not found or expired.' });
        if (entry.paid) return res.json({ success: true, alreadyCredited: true });

        // Try Daraja Transaction Status to auto-verify
        let verified = false;
        let verifiedAmount = 0;
        try {
            const mpesaToken = await getMpesaToken();
            const tsRes = await axios.post(
                'https://api.safaricom.co.ke/mpesa/transactionstatus/v1/query',
                {
                    Initiator: 'testapi',
                    SecurityCredential: process.env.SECURITY_CREDENTIAL || '',
                    CommandID: 'TransactionStatusQuery',
                    TransactionID: receiptNumber.toUpperCase(),
                    PartyA: SHORTCODE,
                    IdentifierType: '4',
                    ResultURL: `${BASE_URL}/mpesa/result`,
                    QueueTimeOutURL: `${BASE_URL}/mpesa/timeout`,
                    Remarks: 'TCP verify',
                    Occasion: ''
                },
                { headers: { Authorization: `Bearer ${mpesaToken}` } }
            );
            console.log('Transaction status:', JSON.stringify(tsRes.data));
            // If ResponseCode is 0, request accepted - result comes via callback
            // For now mark as pending with receipt
            if (tsRes.data?.ResponseCode === '0') {
                entry.pendingReceipt = receiptNumber.toUpperCase();
                await saveTokens(tokens);
                return res.json({ success: true, pending: true, message: 'Verification in progress. Click Verify again in 30 seconds.' });
            }
        } catch (darajaErr) {
            console.log('Daraja verify failed, using manual flow:', darajaErr.response?.data?.errorMessage || darajaErr.message);
        }

        // Fallback: store receipt for admin to verify manually
        entry.pendingReceipt = receiptNumber.toUpperCase();
        await saveTokens(tokens);
        console.log(`Receipt ${receiptNumber} submitted for token ${token}`);
        res.json({ success: true, pending: true, message: 'Receipt submitted. Admin will verify and credit your wallet shortly.' });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// POST /verify-payment
app.post('/verify-payment', async (req, res) => {
    const { token, hardwareId } = req.body;
    if (!token || !hardwareId) return res.status(400).json({ error: 'token and hardwareId required' });

    try {
        const tokens = await getTokens();
        const entry = tokens.find(t => t.token === token.toUpperCase() && t.hardwareId === hardwareId);

        if (!entry) return res.status(404).json({ error: 'Token not found or expired. Please generate a new token.' });
        if (!entry.paid) return res.status(402).json({ error: 'Payment not yet confirmed. If you have already paid, please wait a moment and try again. Contact support if this persists.' });

        // Credit wallet
        const wallets = await getWallets();
        let wallet = wallets.find(w => w.hardwareId === hardwareId);
        if (!wallet) { wallet = { hardwareId, balanceKes: 0, transactions: [] }; wallets.push(wallet); }

        wallet.balanceKes = (wallet.balanceKes || 0) + entry.amount;
        wallet.lastTopUp = new Date().toISOString();
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({ type: 'topup', amount: entry.amount, mpesaRef: entry.mpesaRef, token, date: new Date().toISOString() });

        await saveWallets(wallets);

        // Remove used token
        await saveTokens(tokens.filter(t => t.token !== token.toUpperCase()));

        res.json({ success: true, newBalance: wallet.balanceKes, amount: entry.amount });
    } catch (err) { res.status(500).json({ error: err.message }); }
});

// GET /admin/test-pull - test Pull Transactions API with a receipt
app.post('/admin/test-pull', async (req, res) => {
    const { receipt } = req.body;
    try {
        const token = await getMpesaToken();
        // Try Pull Transactions API
        const pullRes = await axios.post(
            'https://api.safaricom.co.ke/pulltransactions/v1/query',
            {
                ShortCode: SHORTCODE,
                StartDate: new Date(Date.now() - 7*24*60*60*1000).toISOString().slice(0,10).replace(/-/g,''),
                EndDate: new Date().toISOString().slice(0,10).replace(/-/g,''),
                OffSetValue: '0'
            },
            { headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } }
        );
        // Find the specific receipt
        const transactions = pullRes.data?.Response?.Transactions || pullRes.data?.transactions || pullRes.data;
        const match = Array.isArray(transactions) 
            ? transactions.find(t => t.TransID === receipt || t.ReceiptNo === receipt)
            : null;
        res.json({ raw: pullRes.data, match, receipt });
    } catch (err) {
        res.json({ error: err.response?.data || err.message, status: err.response?.status });
    }
});

// POST /mpesa/result - Transaction Status result callback
app.post('/mpesa/result', async (req, res) => {
    try {
        const result = req.body?.Result;
        if (!result) return res.json({ ResultCode: 0, ResultDesc: 'OK' });

        const receiptNumber = result.TransactionID;
        const resultCode = result.ResultCode;
        console.log(`Transaction status result: ${receiptNumber}, code: ${resultCode}`);

        if (resultCode === 0) {
            // Find token with this pending receipt and mark as paid
            const params = result.ResultParameters?.ResultParameter || [];
            const amount = params.find(p => p.Key === 'Amount')?.Value || 0;
            const tokens = await getTokens();
            const entry = tokens.find(t => t.pendingReceipt === receiptNumber?.toUpperCase());
            if (entry) {
                entry.paid = true;
                entry.amount = parseFloat(amount);
                entry.mpesaRef = receiptNumber;
                await saveTokens(tokens);
                console.log(`Auto-credited token ${entry.token}: KES ${amount}`);
            }
        }
        res.json({ ResultCode: 0, ResultDesc: 'OK' });
    } catch (err) {
        console.error('Result callback error:', err.message);
        res.json({ ResultCode: 0, ResultDesc: 'OK' });
    }
});

// POST /mpesa/timeout
app.post('/mpesa/timeout', (req, res) => res.json({ ResultCode: 0, ResultDesc: 'OK' }));

// POST /mpesa/validate
app.post('/mpesa/validate', async (req, res) => {
    const token = (req.body.BillRefNumber || '').toUpperCase().trim();
    if (!token.startsWith('TCP-')) {
        // Forward validation to amorwifi
        try {
            const fwd = await axios.post('https://amorwifi.co.ke/subscription/payment/confirmation/', req.body, { timeout: 5000 });
            return res.json(fwd.data);
        } catch { }
    }
    res.json({ ResultCode: 0, ResultDesc: 'Accepted' });
});

// POST /mpesa/confirm - Safaricom C2B callback - routes TCP payments here, forwards others to amorwifi
app.post('/mpesa/confirm', async (req, res) => {
    const AMORWIFI_URL = 'https://amorwifi.co.ke/subscription/payment/confirmation/';
    try {
        const { BillRefNumber, TransAmount, MpesaReceiptNumber, MSISDN } = req.body;
        const token = (BillRefNumber || '').toUpperCase().trim();
        const amount = parseFloat(TransAmount) || 0;
        console.log(`C2B confirm: token=${token}, amount=${amount}, ref=${MpesaReceiptNumber}`);

        if (token.startsWith('TCP-')) {
            // Handle TelnetCommanderPro payment
            const tokens = await getTokens();
            const entry = tokens.find(t => t.token === token);
            if (entry) {
                entry.paid = true;
                entry.amount = amount;
                entry.mpesaRef = MpesaReceiptNumber;
                entry.phone = MSISDN;
                await saveTokens(tokens);
                console.log(`Token ${token} marked paid: KES ${amount}`);
            } else {
                console.log(`Unknown TCP token: ${token}`);
            }
        } else {
            // Forward to amorwifi system
            try {
                await axios.post(AMORWIFI_URL, req.body, { timeout: 10000 });
                console.log(`Forwarded non-TCP payment to amorwifi: ${token}`);
            } catch (fwdErr) {
                console.error(`Forward to amorwifi failed: ${fwdErr.message}`);
            }
        }

        res.json({ ResultCode: 0, ResultDesc: 'Accepted' });
    } catch (err) {
        console.error('Confirm error:', err.message);
        res.json({ ResultCode: 0, ResultDesc: 'Accepted' });
    }
});

// POST /admin/credit - manually credit a token
app.post('/admin/credit', async (req, res) => {
    const { token, amount, hardwareId } = req.body;
    if (!token || !amount || !hardwareId) return res.status(400).json({ error: 'token, amount and hardwareId required' });

    try {
        const tokens = await getTokens();
        let entry = tokens.find(t => t.token === token.toUpperCase());

        if (!entry) {
            // Create entry if not found (for payments made before token was saved)
            entry = { token: token.toUpperCase(), hardwareId, createdAt: Date.now(), paid: false, amount: 0, mpesaRef: 'ADMIN' };
            tokens.push(entry);
        }

        entry.paid = true;
        entry.amount = parseFloat(amount);
        entry.mpesaRef = 'ADMIN-CREDIT';
        entry.hardwareId = hardwareId;
        await saveTokens(tokens);

        res.json({ success: true, message: `Token ${token} credited KES ${amount} for ${hardwareId}` });
    } catch (err) { res.status(500).json({ error: err.message }); }
});

// POST /deduct
app.post('/deduct', async (req, res) => {
    const { hardwareId, routerType } = req.body;
    if (!hardwareId || !routerType) return res.status(400).json({ error: 'hardwareId and routerType required' });
    const cost = routerType === 'X6' ? 150 : 100;
    try {
        const wallets = await getWallets();
        const wallet = wallets.find(w => w.hardwareId === hardwareId);
        if (!wallet || wallet.balanceKes < cost) {
            return res.status(402).json({ error: 'Insufficient balance', required: cost, balance: wallet?.balanceKes || 0 });
        }
        wallet.balanceKes -= cost;
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({ type: 'deduct', amount: cost, router: routerType, date: new Date().toISOString() });
        await saveWallets(wallets);
        res.json({ success: true, newBalance: wallet.balanceKes });
    } catch (err) { res.status(500).json({ error: err.message }); }
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`Server running on port ${PORT}`));
