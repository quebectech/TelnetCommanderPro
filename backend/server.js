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
const WALLET_BIN_ID   = process.env.WALLET_BIN_ID || '69d61f3736566621a88dd80e';
const JSONBIN_URL     = 'https://api.jsonbin.io/v3/b';
const BASE_URL        = process.env.CALLBACK_URL?.replace('/mpesa/callback', '') || 'https://telnetcommanderpro.onrender.com';

// ── In-memory pending tokens: token → { hardwareId, amount, createdAt } ─────
const pendingTokens = {};

// ── Helpers ──────────────────────────────────────────────────────────────────
function generateToken() {
    return 'TCP-' + crypto.randomBytes(3).toString('hex').toUpperCase();
}

async function getWallets() {
    const res = await axios.get(`${JSONBIN_URL}/${WALLET_BIN_ID}/latest`, {
        headers: { 'X-Master-Key': JSONBIN_API_KEY }
    });
    return res.data.record.wallets || [];
}

async function saveWallets(wallets) {
    await axios.put(`${JSONBIN_URL}/${WALLET_BIN_ID}`, { wallets }, {
        headers: { 'X-Master-Key': JSONBIN_API_KEY, 'Content-Type': 'application/json' }
    });
}

async function getMpesaToken() {
    const auth = Buffer.from(`${CONSUMER_KEY}:${CONSUMER_SECRET}`).toString('base64');
    const res = await axios.get(
        'https://api.safaricom.co.ke/oauth/v1/generate?grant_type=client_credentials',
        { headers: { Authorization: `Basic ${auth}` } }
    );
    return res.data.access_token;
}

// Register C2B callback URL with Safaricom (call once on startup)
async function registerC2BUrl() {
    try {
        const token = await getMpesaToken();
        await axios.post(
            'https://api.safaricom.co.ke/mpesa/c2b/v2/registerurl',
            {
                ShortCode: SHORTCODE,
                ResponseType: 'Completed',
                ConfirmationURL: `${BASE_URL}/mpesa/confirm`,
                ValidationURL: `${BASE_URL}/mpesa/validate`
            },
            { headers: { Authorization: `Bearer ${token}` } }
        );
        console.log('C2B URLs registered successfully');
    } catch (err) {
        console.log('C2B URL registration skipped (may already be registered):', err.response?.data?.errorMessage || err.message);
    }
}

// ── Routes ───────────────────────────────────────────────────────────────────

app.get('/', (req, res) => res.json({ status: 'TelnetCommanderPro backend running' }));

// GET wallet balance
app.get('/wallet/:hardwareId', async (req, res) => {
    try {
        const wallets = await getWallets();
        const wallet = wallets.find(w => w.hardwareId === req.params.hardwareId);
        res.json(wallet || { hardwareId: req.params.hardwareId, balanceKes: 0, transactions: [] });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// POST /generate-token - generate a payment token for manual M-Pesa payment
app.post('/generate-token', async (req, res) => {
    const { hardwareId } = req.body;
    if (!hardwareId) return res.status(400).json({ error: 'hardwareId required' });

    const token = generateToken();
    pendingTokens[token] = {
        hardwareId,
        createdAt: Date.now()
    };

    // Clean up tokens older than 30 minutes
    const cutoff = Date.now() - 30 * 60 * 1000;
    Object.keys(pendingTokens).forEach(k => {
        if (pendingTokens[k].createdAt < cutoff) delete pendingTokens[k];
    });

    res.json({
        token,
        paybill: SHORTCODE,
        instructions: `Send M-Pesa to Paybill ${SHORTCODE}, Account: ${token}`
    });
});

// POST /verify-payment - user clicks "I've Paid", check if C2B callback was received
app.post('/verify-payment', async (req, res) => {
    const { token, hardwareId } = req.body;
    if (!token || !hardwareId) return res.status(400).json({ error: 'token and hardwareId required' });

    const pending = pendingTokens[token];
    if (!pending) return res.status(404).json({ error: 'Token not found or expired. Please generate a new token.' });
    if (pending.hardwareId !== hardwareId) return res.status(403).json({ error: 'Token does not match this device.' });

    // Check if payment was received (set by C2B callback)
    if (!pending.paid) {
        return res.status(402).json({ error: 'Payment not yet received. Please send M-Pesa first then try again.' });
    }

    // Payment confirmed - credit wallet
    try {
        const wallets = await getWallets();
        let wallet = wallets.find(w => w.hardwareId === hardwareId);
        if (!wallet) {
            wallet = { hardwareId, balanceKes: 0, transactions: [] };
            wallets.push(wallet);
        }

        wallet.balanceKes = (wallet.balanceKes || 0) + pending.amount;
        wallet.lastTopUp = new Date().toISOString();
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({
            type: 'topup',
            amount: pending.amount,
            mpesaRef: pending.mpesaRef || '',
            token,
            date: new Date().toISOString()
        });

        await saveWallets(wallets);
        delete pendingTokens[token];

        res.json({ success: true, newBalance: wallet.balanceKes, amount: pending.amount });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// POST /mpesa/validate - Safaricom calls this before confirming C2B payment
app.post('/mpesa/validate', (req, res) => {
    res.json({ ResultCode: 0, ResultDesc: 'Accepted' });
});

// POST /mpesa/confirm - Safaricom calls this after C2B payment is confirmed
app.post('/mpesa/confirm', (req, res) => {
    try {
        const { BillRefNumber, TransAmount, MpesaReceiptNumber, MSISDN } = req.body;
        const token = (BillRefNumber || '').toUpperCase().trim();
        const amount = parseFloat(TransAmount) || 0;

        console.log(`C2B payment received: token=${token}, amount=${amount}, ref=${MpesaReceiptNumber}`);

        if (pendingTokens[token]) {
            pendingTokens[token].paid = true;
            pendingTokens[token].amount = amount;
            pendingTokens[token].mpesaRef = MpesaReceiptNumber;
            pendingTokens[token].phone = MSISDN;
            console.log(`Token ${token} marked as paid: KES ${amount}`);
        } else {
            console.log(`Unknown token received: ${token}`);
        }

        res.json({ ResultCode: 0, ResultDesc: 'Accepted' });
    } catch (err) {
        console.error('Confirm error:', err.message);
        res.json({ ResultCode: 0, ResultDesc: 'Accepted' });
    }
});

// POST /deduct - deduct from wallet after operation
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
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
    console.log(`Server running on port ${PORT}`);
    // Try to register C2B URLs on startup
    registerC2BUrl();
});

