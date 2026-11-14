/* The parts of .NET's Android crypto PAL (libSystem.Security.Cryptography.Native.Android) that the
 * game uses: digests (MD5/SHA-1/SHA-256), HMAC, AES (ECB/CBC) and random bytes. The real library
 * calls into Java and needs a JVM; these are self-contained. FF9Engine's resolver points the
 * library name at the host executable. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/random.h>

#define EXPORT __attribute__((visibility("default")))

/* ---------------- digests ---------------- */
typedef struct { int kind; int size; int block; } MdInfo;     /* kind: 1 md5, 2 sha1, 3 sha256 */
static const MdInfo MD5 = {1, 16, 64}, SHA1 = {2, 20, 64}, SHA256 = {3, 32, 64};

typedef struct {
    const MdInfo *md;
    uint64_t len;
    uint32_t h[8];
    uint8_t buf[64];
    int n;
} MdCtx;

#define ROL(x, n) (((x) << (n)) | ((x) >> (32 - (n))))
#define ROR(x, n) (((x) >> (n)) | ((x) << (32 - (n))))

static void md5_block(uint32_t *h, const uint8_t *p) {
    static const uint32_t K[64] = {
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501, 0x698098d8, 0x8b44f7af, 0xffff5bb1,
        0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821, 0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453,
        0xd8a1e681, 0xe7d3fbc8, 0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a, 0xfffa3942,
        0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70, 0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05,
        0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665, 0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d,
        0x85845dd1, 0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391};
    static const int R[64] = {7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
                              4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21};
    uint32_t w[16];
    for (int i = 0; i < 16; i++) w[i] = p[i * 4] | p[i * 4 + 1] << 8 | p[i * 4 + 2] << 16 | (uint32_t)p[i * 4 + 3] << 24;
    uint32_t a = h[0], b = h[1], c = h[2], d = h[3];
    for (int i = 0; i < 64; i++) {
        uint32_t f; int g;
        if (i < 16) { f = (b & c) | (~b & d); g = i; }
        else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) & 15; }
        else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) & 15; }
        else { f = c ^ (b | ~d); g = (7 * i) & 15; }
        uint32_t t = d; d = c; c = b;
        b = b + ROL(a + f + K[i] + w[g], R[i]);
        a = t;
    }
    h[0] += a; h[1] += b; h[2] += c; h[3] += d;
}

static void sha1_block(uint32_t *h, const uint8_t *p) {
    uint32_t w[80];
    for (int i = 0; i < 16; i++) w[i] = (uint32_t)p[i * 4] << 24 | p[i * 4 + 1] << 16 | p[i * 4 + 2] << 8 | p[i * 4 + 3];
    for (int i = 16; i < 80; i++) w[i] = ROL(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
    uint32_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4];
    for (int i = 0; i < 80; i++) {
        uint32_t f, k;
        if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
        else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
        else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
        else { f = b ^ c ^ d; k = 0xCA62C1D6; }
        uint32_t t = ROL(a, 5) + f + e + k + w[i];
        e = d; d = c; c = ROL(b, 30); b = a; a = t;
    }
    h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e;
}

static void sha256_block(uint32_t *h, const uint8_t *p) {
    static const uint32_t K[64] = {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be,
        0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa,
        0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85,
        0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
        0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f,
        0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};
    uint32_t w[64];
    for (int i = 0; i < 16; i++) w[i] = (uint32_t)p[i * 4] << 24 | p[i * 4 + 1] << 16 | p[i * 4 + 2] << 8 | p[i * 4 + 3];
    for (int i = 16; i < 64; i++) {
        uint32_t s0 = ROR(w[i - 15], 7) ^ ROR(w[i - 15], 18) ^ (w[i - 15] >> 3);
        uint32_t s1 = ROR(w[i - 2], 17) ^ ROR(w[i - 2], 19) ^ (w[i - 2] >> 10);
        w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }
    uint32_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
    for (int i = 0; i < 64; i++) {
        uint32_t S1 = ROR(e, 6) ^ ROR(e, 11) ^ ROR(e, 25), ch = (e & f) ^ (~e & g);
        uint32_t t1 = hh + S1 + ch + K[i] + w[i];
        uint32_t S0 = ROR(a, 2) ^ ROR(a, 13) ^ ROR(a, 22), mj = (a & b) ^ (a & c) ^ (b & c);
        uint32_t t2 = S0 + mj;
        hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
    }
    h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
}

static void md_init(MdCtx *c, const MdInfo *md) {
    memset(c, 0, sizeof *c);
    c->md = md;
    if (md->kind == 1) { c->h[0] = 0x67452301; c->h[1] = 0xefcdab89; c->h[2] = 0x98badcfe; c->h[3] = 0x10325476; }
    else if (md->kind == 2) { c->h[0] = 0x67452301; c->h[1] = 0xEFCDAB89; c->h[2] = 0x98BADCFE; c->h[3] = 0x10325476; c->h[4] = 0xC3D2E1F0; }
    else {
        static const uint32_t iv[8] = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};
        memcpy(c->h, iv, sizeof iv);
    }
}

static void md_block(MdCtx *c, const uint8_t *p) {
    if (c->md->kind == 1) md5_block(c->h, p); else if (c->md->kind == 2) sha1_block(c->h, p); else sha256_block(c->h, p);
}

static void md_update(MdCtx *c, const uint8_t *d, size_t n) {
    c->len += n;
    while (n > 0) {
        size_t k = 64 - c->n;
        if (k > n) k = n;
        memcpy(c->buf + c->n, d, k);
        c->n += k; d += k; n -= k;
        if (c->n == 64) { md_block(c, c->buf); c->n = 0; }
    }
}

static void md_final(const MdCtx *in, uint8_t *out) {
    MdCtx c = *in;
    uint64_t bits = c.len * 8;
    uint8_t pad = 0x80;
    md_update(&c, &pad, 1);
    uint8_t z = 0;
    while (c.n != 56) md_update(&c, &z, 1);
    uint8_t L[8];
    for (int i = 0; i < 8; i++) L[i] = c.md->kind == 1 ? (uint8_t)(bits >> (8 * i)) : (uint8_t)(bits >> (56 - 8 * i));
    md_update(&c, L, 8);
    int words = c.md->size / 4;
    for (int i = 0; i < words; i++)
        for (int k = 0; k < 4; k++) out[i * 4 + k] = c.md->kind == 1 ? (uint8_t)(c.h[i] >> (8 * k)) : (uint8_t)(c.h[i] >> (24 - 8 * k));
}

EXPORT const MdInfo *CryptoNative_EvpMd5(void) { return &MD5; }
EXPORT const MdInfo *CryptoNative_EvpSha1(void) { return &SHA1; }
EXPORT const MdInfo *CryptoNative_EvpSha256(void) { return &SHA256; }
EXPORT const MdInfo *CryptoNative_EvpSha384(void) { fprintf(stderr, "SHA-384 not implemented\n"); return &SHA256; }
EXPORT const MdInfo *CryptoNative_EvpSha512(void) { fprintf(stderr, "SHA-512 not implemented\n"); return &SHA256; }
EXPORT int CryptoNative_EvpMdSize(const MdInfo *md) { return md->size; }
EXPORT MdCtx *CryptoNative_EvpMdCtxCreate(const MdInfo *md) { MdCtx *c = malloc(sizeof *c); md_init(c, md); return c; }
EXPORT void CryptoNative_EvpMdCtxDestroy(MdCtx *c) { free(c); }
EXPORT int CryptoNative_EvpDigestReset(MdCtx *c, const MdInfo *md) { md_init(c, md ? md : c->md); return 1; }
EXPORT int CryptoNative_EvpDigestUpdate(MdCtx *c, const uint8_t *d, int n) { md_update(c, d, n); return 1; }
EXPORT int CryptoNative_EvpDigestFinalEx(MdCtx *c, uint8_t *md, uint32_t *s) { md_final(c, md); *s = c->md->size; md_init(c, c->md); return 1; }
EXPORT int CryptoNative_EvpDigestCurrent(MdCtx *c, uint8_t *md, uint32_t *s) { md_final(c, md); *s = c->md->size; return 1; }
EXPORT int CryptoNative_EvpDigestOneShot(const MdInfo *md, const uint8_t *src, int n, uint8_t *out, uint32_t *s) {
    MdCtx c; md_init(&c, md); md_update(&c, src, n); md_final(&c, out); *s = md->size; return 1;
}

/* ---------------- HMAC ---------------- */
typedef struct { MdCtx inner, outer, inner0; } HmacCtx;

static void hmac_init(HmacCtx *h, const uint8_t *key, int keyLen, const MdInfo *md) {
    uint8_t k[64] = {0}, pad[64];
    if (keyLen > 64) { MdCtx t; md_init(&t, md); md_update(&t, key, keyLen); md_final(&t, k); }
    else if (keyLen > 0) memcpy(k, key, keyLen);
    for (int i = 0; i < 64; i++) pad[i] = k[i] ^ 0x36;
    md_init(&h->inner, md); md_update(&h->inner, pad, 64);
    for (int i = 0; i < 64; i++) pad[i] = k[i] ^ 0x5c;
    md_init(&h->outer, md); md_update(&h->outer, pad, 64);
    h->inner0 = h->inner;
}

static void hmac_final(HmacCtx *h, uint8_t *out) {
    uint8_t ih[32];
    md_final(&h->inner, ih);
    MdCtx o = h->outer;
    md_update(&o, ih, h->inner.md->size);
    md_final(&o, out);
}

EXPORT HmacCtx *CryptoNative_HmacCreate(const uint8_t *key, int keyLen, const MdInfo *md) {
    HmacCtx *h = malloc(sizeof *h);
    hmac_init(h, key, keyLen, md);
    return h;
}
EXPORT void CryptoNative_HmacDestroy(HmacCtx *h) { free(h); }
EXPORT int CryptoNative_HmacReset(HmacCtx *h) { h->inner = h->inner0; return 1; }
EXPORT int CryptoNative_HmacUpdate(HmacCtx *h, const uint8_t *d, int n) { md_update(&h->inner, d, n); return 1; }
EXPORT int CryptoNative_HmacFinal(HmacCtx *h, uint8_t *out, int *len) { hmac_final(h, out); *len = h->inner.md->size; h->inner = h->inner0; return 1; }
EXPORT int CryptoNative_HmacCurrent(HmacCtx *h, uint8_t *out, int *len) { hmac_final(h, out); *len = h->inner.md->size; return 1; }
EXPORT int CryptoNative_HmacOneShot(const MdInfo *md, const uint8_t *key, int keyLen, const uint8_t *src, int n, uint8_t *out, int *len) {
    HmacCtx h; hmac_init(&h, key, keyLen, md); md_update(&h.inner, src, n); hmac_final(&h, out); *len = md->size; return 1;
}

EXPORT int CryptoNative_GetRandomBytes(uint8_t *buf, int n) {
    while (n > 0) { ssize_t r = getrandom(buf, n, 0); if (r <= 0) return 0; buf += r; n -= r; }
    return 1;
}

/* ---------------- AES ---------------- */
static uint8_t sbox[256], inv_sbox[256];
static int aes_ready;

static uint8_t xt(uint8_t x) { return (uint8_t)((x << 1) ^ ((x & 0x80) ? 0x1b : 0)); }
static uint8_t gmul(uint8_t a, uint8_t b) { uint8_t p = 0; while (b) { if (b & 1) p ^= a; a = xt(a); b >>= 1; } return p; }

static void aes_tables(void) {
    if (aes_ready) return;
    uint8_t p = 1, q = 1;
    do {
        p = p ^ (uint8_t)(p << 1) ^ ((p & 0x80) ? 0x1B : 0);
        q ^= q << 1; q ^= q << 2; q ^= q << 4; if (q & 0x80) q ^= 0x09;
        uint8_t x = q ^ (uint8_t)((q << 1) | (q >> 7)) ^ (uint8_t)((q << 2) | (q >> 6)) ^ (uint8_t)((q << 3) | (q >> 5)) ^ (uint8_t)((q << 4) | (q >> 4));
        sbox[p] = x ^ 0x63;
    } while (p != 1);
    sbox[0] = 0x63;
    for (int i = 0; i < 256; i++) inv_sbox[sbox[i]] = (uint8_t)i;
    aes_ready = 1;
}

typedef struct { int keybits, mode; } CipherInfo;   /* mode: 1 ECB, 2 CBC */
static const CipherInfo A128E = {128, 1}, A128C = {128, 2}, A192E = {192, 1}, A192C = {192, 2}, A256E = {256, 1}, A256C = {256, 2}, UNSUP = {0, 0};

typedef struct {
    const CipherInfo *ci;
    uint8_t rk[240];
    int rounds, enc, padding;
    uint8_t iv[16], buf[16];
    int n;
    uint8_t last[16]; int haveLast;   /* decryption keeps the last block back for unpadding */
} CipherCtx;

static void key_expand(CipherCtx *c, const uint8_t *key) {
    int nk = c->ci->keybits / 32;
    c->rounds = nk + 6;
    memcpy(c->rk, key, nk * 4);
    uint8_t rcon = 1;
    for (int i = nk; i < 4 * (c->rounds + 1); i++) {
        uint8_t t[4];
        memcpy(t, c->rk + (i - 1) * 4, 4);
        if (i % nk == 0) {
            uint8_t u = t[0]; t[0] = sbox[t[1]] ^ rcon; t[1] = sbox[t[2]]; t[2] = sbox[t[3]]; t[3] = sbox[u];
            rcon = xt(rcon);
        } else if (nk > 6 && i % nk == 4) { for (int k = 0; k < 4; k++) t[k] = sbox[t[k]]; }
        for (int k = 0; k < 4; k++) c->rk[i * 4 + k] = c->rk[(i - nk) * 4 + k] ^ t[k];
    }
}

static void enc_block(const CipherCtx *c, uint8_t *s) {
    for (int i = 0; i < 16; i++) s[i] ^= c->rk[i];
    for (int r = 1; r <= c->rounds; r++) {
        for (int i = 0; i < 16; i++) s[i] = sbox[s[i]];
        uint8_t t[16];
        for (int col = 0; col < 4; col++) for (int row = 0; row < 4; row++) t[col * 4 + row] = s[((col + row) % 4) * 4 + row];
        if (r != c->rounds)
            for (int col = 0; col < 4; col++) {
                uint8_t *a = t + col * 4, a0 = a[0], a1 = a[1], a2 = a[2], a3 = a[3];
                a[0] = xt(a0) ^ (xt(a1) ^ a1) ^ a2 ^ a3; a[1] = a0 ^ xt(a1) ^ (xt(a2) ^ a2) ^ a3;
                a[2] = a0 ^ a1 ^ xt(a2) ^ (xt(a3) ^ a3); a[3] = (xt(a0) ^ a0) ^ a1 ^ a2 ^ xt(a3);
            }
        for (int i = 0; i < 16; i++) s[i] = t[i] ^ c->rk[r * 16 + i];
    }
}

static void dec_block(const CipherCtx *c, uint8_t *s) {
    for (int i = 0; i < 16; i++) s[i] ^= c->rk[c->rounds * 16 + i];
    for (int r = c->rounds - 1; r >= 0; r--) {
        uint8_t t[16];
        for (int col = 0; col < 4; col++) for (int row = 0; row < 4; row++) t[((col + row) % 4) * 4 + row] = s[col * 4 + row];
        for (int i = 0; i < 16; i++) t[i] = inv_sbox[t[i]] ^ c->rk[r * 16 + i];
        if (r != 0)
            for (int col = 0; col < 4; col++) {
                uint8_t *a = t + col * 4, a0 = a[0], a1 = a[1], a2 = a[2], a3 = a[3];
                a[0] = gmul(a0, 14) ^ gmul(a1, 11) ^ gmul(a2, 13) ^ gmul(a3, 9);
                a[1] = gmul(a0, 9) ^ gmul(a1, 14) ^ gmul(a2, 11) ^ gmul(a3, 13);
                a[2] = gmul(a0, 13) ^ gmul(a1, 9) ^ gmul(a2, 14) ^ gmul(a3, 11);
                a[3] = gmul(a0, 11) ^ gmul(a1, 13) ^ gmul(a2, 9) ^ gmul(a3, 14);
            }
        memcpy(s, t, 16);
    }
}

static void process(CipherCtx *c, const uint8_t *in, uint8_t *out) {
    uint8_t b[16];
    memcpy(b, in, 16);
    if (c->enc) {
        if (c->ci->mode == 2) for (int i = 0; i < 16; i++) b[i] ^= c->iv[i];
        enc_block(c, b);
        if (c->ci->mode == 2) memcpy(c->iv, b, 16);
        memcpy(out, b, 16);
    } else {
        dec_block(c, b);
        if (c->ci->mode == 2) { for (int i = 0; i < 16; i++) b[i] ^= c->iv[i]; memcpy(c->iv, in, 16); }
        memcpy(out, b, 16);
    }
}

#define CIPHER(name, v) EXPORT const CipherInfo *AndroidCryptoNative_##name(void) { return &v; }
CIPHER(Aes128Ecb, A128E) CIPHER(Aes128Cbc, A128C) CIPHER(Aes192Ecb, A192E) CIPHER(Aes192Cbc, A192C) CIPHER(Aes256Ecb, A256E) CIPHER(Aes256Cbc, A256C)
CIPHER(Aes128Gcm, UNSUP) CIPHER(Aes128Cfb8, UNSUP) CIPHER(Aes128Cfb128, UNSUP) CIPHER(Aes128Ccm, UNSUP) CIPHER(Aes192Gcm, UNSUP) CIPHER(Aes192Cfb8, UNSUP)
CIPHER(Aes192Cfb128, UNSUP) CIPHER(Aes192Ccm, UNSUP) CIPHER(Aes256Gcm, UNSUP) CIPHER(Aes256Cfb128, UNSUP) CIPHER(Aes256Cfb8, UNSUP) CIPHER(Aes256Ccm, UNSUP)

EXPORT int AndroidCryptoNative_CipherIsSupported(const CipherInfo *ci) { return ci->mode != 0; }

EXPORT CipherCtx *AndroidCryptoNative_CipherCreatePartial(const CipherInfo *ci) {
    aes_tables();
    CipherCtx *c = calloc(1, sizeof *c);
    c->ci = ci;
    c->padding = 1;
    return c;
}

EXPORT int AndroidCryptoNative_CipherSetKeyAndIV(CipherCtx *c, const uint8_t *key, const uint8_t *iv, int direction) {
    if (direction >= 0) c->enc = direction;
    if (key) key_expand(c, key);
    if (iv) memcpy(c->iv, iv, 16);
    c->n = 0; c->haveLast = 0;
    return 1;
}

EXPORT CipherCtx *AndroidCryptoNative_CipherCreate(const CipherInfo *ci, const uint8_t *key, int keyBits, const uint8_t *iv, int enc) {
    CipherCtx *c = AndroidCryptoNative_CipherCreatePartial(ci);
    AndroidCryptoNative_CipherSetKeyAndIV(c, key, iv, enc);
    return c;
}

EXPORT void AndroidCryptoNative_CipherDestroy(CipherCtx *c) { free(c); }
EXPORT int AndroidCryptoNative_CipherCtxSetPadding(CipherCtx *c, int padding) { c->padding = padding; return 1; }
EXPORT int AndroidCryptoNative_CipherReset(CipherCtx *c, const uint8_t *iv, int ivLen) { if (iv && ivLen >= 16) memcpy(c->iv, iv, 16); c->n = 0; c->haveLast = 0; return 1; }
EXPORT int AndroidCryptoNative_CipherSetNonceLength(CipherCtx *c, int n) { return 0; }
EXPORT int AndroidCryptoNative_CipherSetTagLength(CipherCtx *c, int n) { return 0; }
EXPORT int AndroidCryptoNative_CipherUpdateAAD(CipherCtx *c, const uint8_t *in, int n) { return 0; }

EXPORT int AndroidCryptoNative_CipherUpdate(CipherCtx *c, uint8_t *out, int *outl, const uint8_t *in, int inl) {
    int o = 0;
    for (int i = 0; i < inl; i++) {
        c->buf[c->n++] = in[i];
        if (c->n == 16) {
            if (!c->enc && c->padding) {           /* hold back one block for unpadding */
                if (c->haveLast) { memcpy(out + o, c->last, 16); o += 16; }
                process(c, c->buf, c->last);
                c->haveLast = 1;
            } else { process(c, c->buf, out + o); o += 16; }
            c->n = 0;
        }
    }
    *outl = o;
    return 1;
}

EXPORT int AndroidCryptoNative_CipherFinalEx(CipherCtx *c, uint8_t *out, int *outl) {
    *outl = 0;
    if (c->enc) {
        if (!c->padding) { c->n = 0; return 1; }
        int p = 16 - c->n;
        for (int i = c->n; i < 16; i++) c->buf[i] = (uint8_t)p;
        process(c, c->buf, out);
        *outl = 16;
    } else if (c->padding && c->haveLast) {
        int p = c->last[15];
        if (p < 1 || p > 16) return 0;
        memcpy(out, c->last, 16 - p);
        *outl = 16 - p;
    }
    c->n = 0; c->haveLast = 0;
    return 1;
}

EXPORT int AndroidCryptoNative_AeadCipherFinalEx(CipherCtx *c, uint8_t *out, int *outl, int *mismatch) { *mismatch = 0; return AndroidCryptoNative_CipherFinalEx(c, out, outl); }

EXPORT int CryptoNative_GetMaxMdSize(void) { return 64; }
EXPORT int CryptoNative_EnsureOpenSslInitialized(void) { return 0; }
