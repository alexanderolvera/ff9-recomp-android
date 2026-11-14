package com.ff9recomp;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.Handler;
import android.os.Looper;
import android.view.View;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The second screen's panels (AYN Thor's bottom screen and other two-screen devices): where the
 * party is, gil, the party's HP/MP (and ATB and Trance in battle) and the enemies' HP. The game
 * sends its state ten times a second (FF9Engine Core/Companion.cs, GameActivity.companionState).
 * Blank until a game is under way; dimmed while a movie plays. Drawn here, no game art.
 */
public class CompanionView extends View {
    private static final int BG = Color.rgb(10, 12, 20), TEXT = Color.rgb(236, 232, 220), DIM = Color.rgb(150, 150, 160);
    private static final int HP = Color.rgb(90, 200, 120), MP = Color.rgb(90, 150, 240), ATB = Color.rgb(240, 200, 80),
            TRANCE = Color.rgb(240, 110, 170), EN = Color.rgb(230, 90, 80);

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG), line = new Paint(Paint.ANTI_ALIAS_FLAG), text = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Typeface serif = Typeface.create(Typeface.SERIF, Typeface.NORMAL), serifBold = Typeface.create(Typeface.SERIF, Typeface.BOLD);
    private JSONObject state = new JSONObject();
    private String last = "";
    private float dim;

    private final Runnable poll = new Runnable() {
        @Override public void run() {
            String s = GameActivity.companionState();
            if (s != null && !s.equals(last)) {
                last = s;
                try { state = new JSONObject(s); } catch (Exception ignored) { }
                invalidate();
            }
            float want = state.optInt("cine") == 1 ? 1f : 0f;
            if (dim != want) {
                dim += Math.signum(want - dim) * 0.25f;
                if (Math.abs(dim - want) < 0.01f) dim = want;
                invalidate();
            }
            handler.postDelayed(this, 100);
        }
    };

    public CompanionView(Context c) {
        super(c);
        line.setStyle(Paint.Style.STROKE);
        text.setTypeface(serif);
    }

    @Override protected void onAttachedToWindow() { super.onAttachedToWindow(); handler.post(poll); }
    @Override protected void onDetachedFromWindow() { handler.removeCallbacks(poll); super.onDetachedFromWindow(); }

    /** The game's text tags (like [STRT=67,1] and [ENDN]) out of a name. */
    static String clean(String s) { return s == null ? "" : s.replaceAll("\\[[A-Z]+(=[^\\]]*)?\\]", "").trim(); }

    @Override
    protected void onDraw(Canvas c) {
        int w = getWidth(), h = getHeight();
        c.drawColor(BG);
        if (state.optInt("ready") != 1) return;     // blank until a game is under way
        float u = Math.min(w, h) / 1000f;
        float pad = 28 * u;
        String mode = state.optString("mode");
        boolean battle = "battle".equals(mode);

        // header: where the party is, gil
        String loc = clean(state.optString("loc"));
        String where = "world".equals(mode) ? (loc.isEmpty() ? "World map" : "World map · " + loc) : loc;
        text.setTypeface(serifBold);
        label(c, where.isEmpty() ? " " : where, pad, pad + 44 * u, 40 * u, TEXT, Paint.Align.LEFT);
        text.setTypeface(serif);
        label(c, String.format(java.util.Locale.US, "%,d Gil", state.optLong("gil")), w - pad, pad + 44 * u, 34 * u, TEXT, Paint.Align.RIGHT);
        float y = pad + 80 * u;

        // the party
        JSONArray party = state.optJSONArray("party");
        int n = party == null ? 0 : party.length();
        float cardH = battle ? 150 * u : 132 * u, gap = 14 * u;
        for (int i = 0; i < n; i++) {
            JSONObject m = party.optJSONObject(i);
            if (m == null) continue;
            RectF r = new RectF(pad, y, w - pad, y + cardH);
            window(c, r, u);
            float x = r.left + 22 * u, right = r.right - 22 * u;
            label(c, clean(m.optString("name")), x, r.top + 44 * u, 38 * u, TEXT, Paint.Align.LEFT);
            label(c, "Lv " + m.optInt("lv"), right, r.top + 44 * u, 30 * u, DIM, Paint.Align.RIGHT);
            float col = (right - x) / 2, bw = col - 24 * u, by = r.top + 76 * u;
            stat(c, "HP", m.optLong("hp"), m.optLong("mhp"), x, by, bw, HP, u);
            stat(c, "MP", m.optLong("mp"), m.optLong("mmp"), x + col + 12 * u, by, bw, MP, u);
            if (battle) {
                double atb = m.optDouble("atb", 0) / Math.max(1, m.optDouble("matb", 1));
                bar(c, x, r.top + 124 * u, bw, 12 * u, atb, atb >= 1 ? Color.WHITE : ATB);
                int t = m.optInt("trance", -1);
                if (t >= 0) bar(c, x + col + 12 * u, r.top + 124 * u, bw, 12 * u, t / 255.0, TRANCE);
            }
            y += cardH + gap;
        }

        // the enemies, in battle
        JSONArray en = battle ? state.optJSONArray("enemies") : null;
        if (en != null && en.length() > 0) {
            y += 10 * u;
            label(c, "Enemies", pad, y + 32 * u, 30 * u, DIM, Paint.Align.LEFT);
            y += 48 * u;
            for (int i = 0; i < en.length() && y < h - 60 * u; i++) {
                JSONObject e = en.optJSONObject(i);
                if (e == null || e.optInt("target") != 1) continue;
                long hp = e.optLong("hp"), mhp = Math.max(1, e.optLong("mhp"));
                label(c, clean(e.optString("name")), pad, y + 30 * u, 30 * u, hp > 0 ? TEXT : DIM, Paint.Align.LEFT);
                label(c, hp + " / " + mhp, w - pad, y + 30 * u, 26 * u, DIM, Paint.Align.RIGHT);
                bar(c, pad, y + 42 * u, w - 2 * pad, 10 * u, hp / (double) mhp, EN);
                y += 70 * u;
            }
        }
        if (dim > 0) {
            fill.setShader(null);
            fill.setColor(Color.argb(Math.round(170 * dim), 0, 0, 0));
            c.drawRect(0, 0, w, h, fill);
        }
    }

    private void window(Canvas c, RectF r, float u) {
        fill.setShader(new LinearGradient(0, r.top, 0, r.bottom, Color.rgb(70, 74, 92), Color.rgb(36, 38, 52), Shader.TileMode.CLAMP));
        c.drawRoundRect(r, 8 * u, 8 * u, fill);
        fill.setShader(null);
        line.setColor(Color.rgb(200, 196, 182));
        line.setStrokeWidth(2.5f * u);
        c.drawRoundRect(r, 8 * u, 8 * u, line);
    }

    private void stat(Canvas c, String name, long v, long max, float x, float y, float bw, int color, float u) {
        label(c, name, x, y, 24 * u, DIM, Paint.Align.LEFT);
        label(c, v + " / " + max, x + bw, y, 28 * u, TEXT, Paint.Align.RIGHT);
        bar(c, x, y + 10 * u, bw, 10 * u, v / (double) Math.max(1, max), color);
    }

    private void bar(Canvas c, float x, float y, float bw, float bh, double f, int color) {
        f = Math.max(0, Math.min(1, f));
        fill.setShader(null);
        fill.setColor(Color.argb(160, 0, 0, 0));
        c.drawRoundRect(x, y, x + bw, y + bh, bh / 2, bh / 2, fill);
        fill.setColor(color);
        if (f > 0) c.drawRoundRect(x, y, x + (float) (bw * f), y + bh, bh / 2, bh / 2, fill);
    }

    private void label(Canvas c, String s, float x, float y, float size, int color, Paint.Align a) {
        text.setTextSize(size);
        text.setColor(color);
        text.setTextAlign(a);
        c.drawText(s, x, y, text);
    }
}
