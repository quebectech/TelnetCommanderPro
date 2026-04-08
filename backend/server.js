const express = require('express');
const axios = require('axios');
const app = express();
app.use(express.json());

// ── Config (set these as Render environment variables) ──────────────────────
const CONSUMER_KEY     = process.env.CONSUMER_KEY;
const CONSUMER_SECRET  = process.env.CONSUMER_SECRET;
const PASSKEY          = process.env.PASSKEY;
const SHORTCODE        = process.env.SHORTCODE || '4167789';
const CALLBACK_URL     = process.env.CALLBACK_URL || 'https://telnetcommanderpro.onrender.com/mpesa/callback';
const JSONBIN_API_KEY  = process.env.JSONBIN_API_KEY;
const WALLET_BIN_ID    = process.env.WALLET_BIN_ID || '69d61f3736566621a88dd80e';
const JSONBIN_URL      = 'https://api.jsonbin.io/v3/b';

// ── In-memory map: checkoutRequestId → hardwareId (cleared after callback) ──
const pendingPayments = {};

// ── Helpers ──────────────────────────────────────────────────────────────────

async function getMpesaToken() {
    const auth = Buffer.from(`${CONSUMER_KEY}:${CONSUMER_SECRET}`).toString('base64');
    const res = await axios.get(
        'https://api.safaricom.co.ke/oauth/v1/generate?grant_type=client_credentials',
        { headers: { Authorization: `Basic ${auth}` } }
    );
    return res.data.access_token;
}

function getTimestamp() {
    return new Date().toISOString().replace(/[-T:.Z]/g, '').slice(0, 14);
}

function getPassword(timestamp) {
    return Buffer.from(`${SHORTCODE}${PASSKEY}${timestamp}`).toString('base64');
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

// ── Routes ───────────────────────────────────────────────────────────────────

// Health check
app.get('/', (req, res) => res.json({ status: 'TelnetCommanderPro backend running' }));

// GET wallet balance by hardwareId
app.get('/wallet/:hardwareId', async (req, res) => {
    try {
        const wallets = await getWallets();
        const wallet = wallets.find(w => w.hardwareId === req.params.hardwareId);
        if (!wallet) {
            return res.json({ hardwareId: req.params.hardwareId, balanceKes: 0, transactions: [] });
        }
        res.json(wallet);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// POST /topup - initiate STK Push
app.post('/topup', async (req, res) => {
    const { hardwareId, phone, amount } = req.body;

    if (!hardwareId || !phone || !amount) {
        return res.status(400).json({ error: 'hardwareId, phone and amount are required' });
    }
    if (amount < 100) {
        return res.status(400).json({ error: 'Minimum top-up is KES 100' });
    }

    // Normalize phone: 07XX → 2547XX
    let msisdn = phone.toString().replace(/\s/g, '');
    if (msisdn.startsWith('0')) msisdn = '254' + msisdn.slice(1);
    if (msisdn.startsWith('+')) msisdn = msisdn.slice(1);

    try {
        const token = await getMpesaToken();
        const timestamp = getTimestamp();
        const password = getPassword(timestamp);

        const stkRes = await axios.post(
            'https://api.safaricom.co.ke/mpesa/stkpush/v1/processrequest',
            {
                BusinessShortCode: SHORTCODE,
                Password: password,
                Timestamp: timestamp,
                TransactionType: 'CustomerPayBillOnline',
                Amount: Math.floor(amount),
                PartyA: msisdn,
                PartyB: SHORTCODE,
                PhoneNumber: msisdn,
                CallBackURL: CALLBACK_URL,
                AccountReference: `TCP-${hardwareId.slice(0, 8)}`,
                TransactionDesc: 'TelnetCommanderPro Top-Up'
            },
            { headers: { Authorization: `Bearer ${token}` } }
        );

        const checkoutRequestId = stkRes.data.CheckoutRequestID;

        // Store mapping so callback knows which wallet to credit
        pendingPayments[checkoutRequestId] = { hardwareId, amount: Math.floor(amount) };

        res.json({
            success: true,
            checkoutRequestId,
            message: 'STK Push sent. Check your phone.'
        });
    } catch (err) {
        const msg = err.response?.data?.errorMessage || err.message;
        res.status(500).json({ error: msg });
    }
});

// POST /mpesa/callback - Safaricom calls this after payment
app.post('/mpesa/callback', async (req, res) => {
    try {
        const body = req.body?.Body?.stkCallback;
        if (!body) return res.json({ ResultCode: 0, ResultDesc: 'OK' });

        const resultCode = body.ResultCode;
        const checkoutRequestId = body.CheckoutRequestID;

        if (resultCode !== 0) {
            // Payment failed or cancelled - just clean up
            delete pendingPayments[checkoutRequestId];
            return res.json({ ResultCode: 0, ResultDesc: 'OK' });
        }

        // Extract amount and M-Pesa receipt
        const items = body.CallbackMetadata?.Item || [];
        const paidAmount = items.find(i => i.Name === 'Amount')?.Value || 0;
        const mpesaRef   = items.find(i => i.Name === 'MpesaReceiptNumber')?.Value || '';

        const pending = pendingPayments[checkoutRequestId];
        if (!pending) return res.json({ ResultCode: 0, ResultDesc: 'OK' });

        const { hardwareId } = pending;
        delete pendingPayments[checkoutRequestId];

        // Credit the wallet
        const wallets = await getWallets();
        let wallet = wallets.find(w => w.hardwareId === hardwareId);

        if (!wallet) {
            wallet = { hardwareId, balanceKes: 0, transactions: [] };
            wallets.push(wallet);
        }

        wallet.balanceKes = (wallet.balanceKes || 0) + paidAmount;
        wallet.lastTopUp = new Date().toISOString();
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({
            type: 'topup',
            amount: paidAmount,
            mpesaRef,
            date: new Date().toISOString()
        });

        await saveWallets(wallets);

        res.json({ ResultCode: 0, ResultDesc: 'OK' });
    } catch (err) {
        console.error('Callback error:', err.message);
        res.json({ ResultCode: 0, ResultDesc: 'OK' }); // always return OK to Safaricom
    }
});

// POST /deduct - called by app after successful operation
app.post('/deduct', async (req, res) => {
    const { hardwareId, routerType } = req.body;
    if (!hardwareId || !routerType) {
        return res.status(400).json({ error: 'hardwareId and routerType required' });
    }

    // Cost per operation
    const cost = routerType === 'X6' ? 150 : 100;

    try {
        const wallets = await getWallets();
        const wallet = wallets.find(w => w.hardwareId === hardwareId);

        if (!wallet || wallet.balanceKes < cost) {
            return res.status(402).json({ error: 'Insufficient balance', required: cost, balance: wallet?.balanceKes || 0 });
        }

        wallet.balanceKes -= cost;
        wallet.transactions = wallet.transactions || [];
        wallet.transactions.push({
            type: 'deduct',
            amount: cost,
            router: routerType,
            date: new Date().toISOString()
        });

        await saveWallets(wallets);
        res.json({ success: true, newBalance: wallet.balanceKes });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => console.log(`Server running on port ${PORT}`));
