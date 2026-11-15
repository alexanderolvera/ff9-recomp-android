package com.ff9recomp;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.Handler;
import android.os.Looper;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;

/**
 * The second screen (AYN Thor's bottom screen), laid out like FF7Recomp's: a header (where, play
 * time, gil, the guide book), a big window (the world map with the party's arrow; the enemies in
 * battle; the place in fields), the party on the right and a command deck: Menu, Save, Load,
 * Speed, Battles, Battle+ (FF9's own boosters, set as the in-game booster menu does). The game
 * sends its state ten times a second (FF9Engine Core/Companion.cs). Blank until a game is under
 * way; cutscenes dim it with a slow ease. FF9's colours (the launcher's windows); no game art
 * except the world map, saved on the device from the player's own copy.
 */
public class CompanionView extends View {
    private static final int BG = Color.rgb(8, 9, 14), TEXT = 0xFFF2EEE2, DIM = 0xFFAEB0BC, ACCENT = 0xFFE8C66A;
    private static final int WIN_TOP = 0xFF5E6478, WIN_BOTTOM = 0xFF22252F, HOT_TOP = 0xFF8A8466, HOT_BOTTOM = 0xFF3E3A2A,
            BORDER = 0xFFDCD6C4, INNER = 0xFF3A3F50, ON_TOP = 0xFF7A7048, ON_BOTTOM = 0xFF3A3420;
    private static final int HP = Color.rgb(96, 204, 120), MP = Color.rgb(96, 150, 240), ATB = Color.rgb(240, 200, 80),
            TRANCE = Color.rgb(240, 110, 170), EN = Color.rgb(230, 90, 80);

    // the deck's commands (Core/Companion.cs; Save and Load go through the app's save states)
    private static final int CMD_SPEED = 1, CMD_SAVE = 2, CMD_LOAD = 3, CMD_BATTLES = 4, CMD_BPLUS = 5, CMD_MENU = 100;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG), line = new Paint(Paint.ANTI_ALIAS_FLAG),
            text = new Paint(Paint.ANTI_ALIAS_FLAG), mapPaint = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
    private final Typeface sans = Typeface.create("sans-serif-medium", Typeface.NORMAL), sansBold = Typeface.create("sans-serif", Typeface.BOLD);
    private JSONObject state = new JSONObject();
    private String last = "";

    private static final class Button {
        final String label; final int cmd; final RectF box = new RectF();
        Button(String label, int cmd) { this.label = label; this.cmd = cmd; }
    }
    private final Button[] buttons = {
        new Button("Menu", CMD_MENU), new Button("Save", CMD_SAVE), new Button("Load", CMD_LOAD),
        new Button("Speed", CMD_SPEED), new Button("Battles", CMD_BATTLES), new Button("Battle+", CMD_BPLUS),
    };
    private Button pressed;
    private boolean deck = true;
    private final RectF bookBox = new RectF();
    private boolean bookPressed;
    private String flash;
    private long flashUntil;

    // cutscene dim, as FF7Recomp: a state holds a moment before the lights change, then eases
    private static final float DIM_MAX = 0.85f;
    private static final long DIM_HOLD_IN = 250, DIM_HOLD_OUT = 400, DIM_IN_MS = 1200, DIM_OUT_MS = 700;
    private float dim;
    private boolean dimTarget, seenCine;
    private long seenSince;
    private android.animation.ValueAnimator dimAnim;

    private final Runnable poll = new Runnable() {
        @Override public void run() {
            String s = GameActivity.companionState();
            if (s != null && !s.equals(last)) {
                last = s;
                try { state = new JSONObject(s); } catch (Exception ignored) { }
                invalidate();
            }
            updateDim(state.optInt("cine") == 1);
            if (flash != null && android.os.SystemClock.uptimeMillis() > flashUntil) { flash = null; invalidate(); }
            handler.postDelayed(this, 100);
        }
    };

    public CompanionView(Context c) {
        super(c);
        line.setStyle(Paint.Style.STROKE);
        setHapticFeedbackEnabled(true);
        deck = Settings.prefs(c).getBoolean("bottom_buttons", true);
    }

    @Override protected void onAttachedToWindow() { super.onAttachedToWindow(); handler.post(poll); }
    @Override protected void onDetachedFromWindow() { handler.removeCallbacks(poll); super.onDetachedFromWindow(); }

    private void updateDim(boolean cine) {
        long now = android.os.SystemClock.uptimeMillis();
        if (cine != seenCine) { seenCine = cine; seenSince = now; }
        if (cine == dimTarget || now - seenSince < (cine ? DIM_HOLD_IN : DIM_HOLD_OUT)) return;
        dimTarget = cine;
        if (dimAnim != null) dimAnim.cancel();
        dimAnim = android.animation.ValueAnimator.ofFloat(dim, cine ? 1f : 0f);
        dimAnim.setDuration((long) ((cine ? DIM_IN_MS : DIM_OUT_MS) * Math.abs((cine ? 1f : 0f) - dim)));
        dimAnim.setInterpolator(new android.view.animation.PathInterpolator(0.45f, 0f, 0.2f, 1f));
        dimAnim.addUpdateListener(a -> { dim = (float) a.getAnimatedValue(); invalidate(); });
        dimAnim.start();
    }

    /** The game's text tags (like [STRT=67,1] and [ENDN]) out of a name. */
    static String clean(String s) { return s == null ? "" : s.replaceAll("\\[[A-Z]+(=[^\\]]*)?\\]", "").trim(); }

    // ------------------------------------------------------------------ drawing

    @Override
    protected void onDraw(Canvas c) {
        int w = getWidth(), h = getHeight();
        c.drawColor(BG);
        for (Button b : buttons) b.box.setEmpty();
        bookBox.setEmpty();
        if (state.optInt("ready") != 1) return;     // blank until a game is under way
        float u = Math.min(w, h) / 1000f, pad = 24 * u, gap = 18 * u;
        String mode = state.optString("mode");
        boolean battle = "battle".equals(mode), world = "world".equals(mode);

        // header: where, play time, gil, the guide book
        RectF head = new RectF(pad, pad, w - pad, pad + 92 * u);
        window(c, head, WIN_TOP, WIN_BOTTOM, u);
        String loc = clean(state.optString("loc"));
        String where = battle ? "Battle" : world ? "World map" : loc.isEmpty() ? " " : loc;
        float right = head.right - 24 * u;
        if (GameActivity.hasGuide()) {
            bookBox.set(right - 64 * u, head.top + 18 * u, right, head.bottom - 18 * u);
            drawBook(c, bookBox, u);
            right = bookBox.left - 24 * u;
        }
        text.setTypeface(sans);
        String gil = String.format(java.util.Locale.US, "%d G", state.optLong("gil"));
        label(c, gil, right, head.centerY() + 13 * u, 36 * u, TEXT, Paint.Align.RIGHT);
        float gilW = text.measureText(gil);
        long t = state.optLong("time");
        label(c, String.format(java.util.Locale.US, "%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60), right - gilW - 36 * u, head.centerY() + 13 * u, 36 * u, TEXT, Paint.Align.RIGHT);
        text.setTypeface(sansBold);
        label(c, ellipsize(where, head.width() * 0.45f, 40 * u), head.left + 24 * u, head.centerY() + 14 * u, 40 * u, TEXT, Paint.Align.LEFT);

        // the deck at the bottom
        float deckH = deck ? 104 * u : 0;
        if (deck) {
            float bw = (w - 2 * pad - 5 * gap) / 6f, by = h - pad - deckH;
            for (int i = 0; i < buttons.length; i++) {
                Button b = buttons[i];
                b.box.set(pad + i * (bw + gap), by, pad + i * (bw + gap) + bw, by + deckH);
                boolean on = (b.cmd == CMD_SPEED && state.optInt("speed") == 1) || (b.cmd == CMD_BATTLES && state.optInt("noenc") == 1)
                        || (b.cmd == CMD_BPLUS && state.optInt("bplus") == 1);
                window(c, b.box, b == pressed ? HOT_TOP : on ? ON_TOP : WIN_TOP, b == pressed ? HOT_BOTTOM : on ? ON_BOTTOM : WIN_BOTTOM, u);
                String l = b.label;
                if (b.cmd == CMD_SPEED) l = state.optInt("speed") == 1 ? "Speed " + speedFactor() + "×" : "Speed 1×";
                else if (b.cmd == CMD_BATTLES) l = state.optInt("noenc") == 1 ? "No battles" : "Battles";
                text.setTypeface(sansBold);
                label(c, l, b.box.centerX(), b.box.centerY() + 12 * u, fit(l, b.box.width() - 16 * u, 32 * u), on ? ACCENT : TEXT, Paint.Align.CENTER);
            }
        }

        // body: the big window and the party column
        float top = head.bottom + gap, bottom = h - pad - deckH - (deck ? gap : 0);
        float colW = (w - 2 * pad) * 0.30f;
        RectF big = new RectF(pad, top, w - pad - colW - gap, bottom);
        window(c, big, WIN_TOP, WIN_BOTTOM, u);
        if (battle) drawEnemies(c, big, u);
        else drawMap(c, big, world, loc, u);
        drawParty(c, new RectF(big.right + gap, top, w - pad, bottom), battle, u);

        if (flash != null) {
            text.setTypeface(sansBold);
            text.setTextSize(30 * u);
            float fw = text.measureText(flash) + 48 * u;
            RectF fr = new RectF(big.centerX() - fw / 2, big.bottom - 84 * u, big.centerX() + fw / 2, big.bottom - 24 * u);
            window(c, fr, HOT_TOP, HOT_BOTTOM, u);
            label(c, flash, fr.centerX(), fr.centerY() + 10 * u, 30 * u, TEXT, Paint.Align.CENTER);
        }
        if (dim > 0) {
            fill.setShader(null);
            fill.setColor(Color.argb(Math.round(255 * DIM_MAX * dim), 0, 0, 0));
            c.drawRect(0, 0, w, h, fill);
        }
    }

    private int speedFactor() { return Settings.get(getContext(), Settings.find("speed")) + 2; }

    private Bitmap map;
    private String mapName = "";

    /** The world map (when the game has saved one), the party's arrow on the world map; in a field the place's name over it. */
    private void drawMap(Canvas c, RectF box, boolean world, String loc, float u) {
        String name = state.optString("wmap", mapName.isEmpty() ? "world_map_full_all" : mapName);
        if (!name.equals(mapName) || map == null) {
            mapName = name;
            File f = new File(new GameInstall(getContext()).files, "worldmap/" + name + ".png");
            map = f.isFile() ? BitmapFactory.decodeFile(f.getPath()) : null;
        }
        RectF in = new RectF(box.left + 10 * u, box.top + 10 * u, box.right - 10 * u, box.bottom - 10 * u);
        if (map != null) {
            float scale = Math.min(in.width() / map.getWidth(), in.height() / map.getHeight());
            float mw = map.getWidth() * scale, mh = map.getHeight() * scale;
            RectF dst = new RectF(in.centerX() - mw / 2, in.centerY() - mh / 2, in.centerX() + mw / 2, in.centerY() + mh / 2);
            mapPaint.setAlpha(world ? 255 : 110);
            c.drawBitmap(map, null, dst, mapPaint);
            if (world && state.has("wx")) {
                float px = dst.left + dst.width() * (float) state.optDouble("wx"), py = dst.top + dst.height() * (1f - (float) state.optDouble("wy"));
                fill.setShader(null);
                fill.setColor(0x66E05050);
                c.drawCircle(px, py, 26 * u, fill);
                c.save();
                c.translate(px, py);
                c.rotate((float) state.optDouble("wrot") + 90f);
                Path arrow = new Path();
                arrow.moveTo(20 * u, 0); arrow.lineTo(-12 * u, -12 * u); arrow.lineTo(-6 * u, 0); arrow.lineTo(-12 * u, 12 * u); arrow.close();
                fill.setColor(0xFFFFE070);
                c.drawPath(arrow, fill);
                line.setColor(0xFF201810); line.setStrokeWidth(2.5f * u);
                c.drawPath(arrow, line);
                c.restore();
            }
        }
        if (!world) {
            text.setTypeface(sansBold);
            String l = loc.isEmpty() ? " " : loc;
            float size = fit(l, in.width() - 40 * u, 44 * u);
            label(c, l, in.centerX(), in.centerY() + 16 * u, size, TEXT, Paint.Align.CENTER);
        }
    }

    private void drawEnemies(Canvas c, RectF box, float u) {
        JSONArray en = state.optJSONArray("enemies");
        float x = box.left + 26 * u, y = box.top + 56 * u, wBar = box.width() - 52 * u;
        text.setTypeface(sansBold);
        label(c, "Enemies", x, y, 32 * u, DIM, Paint.Align.LEFT);
        y += 34 * u;
        if (en == null) return;
        for (int i = 0; i < en.length() && y < box.bottom - 70 * u; i++) {
            JSONObject e = en.optJSONObject(i);
            if (e == null || e.optInt("target") != 1) continue;
            long hp = e.optLong("hp"), mhp = Math.max(1, e.optLong("mhp"));
            text.setTypeface(sans);
            label(c, ellipsize(clean(e.optString("name")), wBar * 0.65f, 34 * u), x, y + 34 * u, 34 * u, hp > 0 ? TEXT : DIM, Paint.Align.LEFT);
            label(c, hp + " / " + mhp, x + wBar, y + 34 * u, 28 * u, DIM, Paint.Align.RIGHT);
            bar(c, x, y + 48 * u, wBar, 12 * u, hp / (double) mhp, EN);
            y += 92 * u;
        }
    }

    private void drawParty(Canvas c, RectF col, boolean battle, float u) {
        JSONArray party = state.optJSONArray("party");
        int n = party == null ? 0 : Math.min(4, party.length());
        if (n == 0) return;
        float g = 14 * u, ch = (col.height() - g * 3) / 4f;
        for (int i = 0; i < n; i++) {
            JSONObject m = party.optJSONObject(i);
            if (m == null) continue;
            RectF r = new RectF(col.left, col.top + i * (ch + g), col.right, col.top + i * (ch + g) + ch);
            window(c, r, WIN_TOP, WIN_BOTTOM, u);
            float x = r.left + 18 * u, bw = r.width() - 36 * u, k = Math.min(1f, ch / (200 * u));
            text.setTypeface(sansBold);
            label(c, ellipsize(clean(m.optString("name")), bw, 34 * k * u), x, r.top + 40 * k * u, 34 * k * u, TEXT, Paint.Align.LEFT);
            text.setTypeface(sans);
            float yy = r.top + 80 * k * u;
            label(c, "HP " + m.optLong("hp"), x, yy, 26 * k * u, TEXT, Paint.Align.LEFT);
            bar(c, x, yy + 8 * u, bw, 9 * u, m.optLong("hp") / (double) Math.max(1, m.optLong("mhp")), HP);
            yy += 52 * k * u;
            label(c, "MP " + m.optLong("mp"), x, yy, 26 * k * u, TEXT, Paint.Align.LEFT);
            bar(c, x, yy + 8 * u, bw, 9 * u, m.optLong("mp") / (double) Math.max(1, m.optLong("mmp")), MP);
            if (battle) {
                yy += 36 * k * u;
                double atb = m.optDouble("atb", 0) / Math.max(1, m.optDouble("matb", 1));
                int tr = m.optInt("trance", -1);
                bar(c, x, yy, bw * (tr >= 0 ? 0.48f : 1f), 9 * u, atb, atb >= 1 ? Color.WHITE : ATB);
                if (tr >= 0) bar(c, x + bw * 0.52f, yy, bw * 0.48f, 9 * u, tr / 255.0, TRANCE);
            }
        }
    }

    private void drawBook(Canvas c, RectF b, float u) {
        fill.setShader(null);
        fill.setColor(bookPressed ? ACCENT : 0xFFF4F0E4);
        float mid = b.centerX();
        Path left = new Path(), right = new Path();
        left.moveTo(mid, b.top + 6 * u); left.lineTo(b.left, b.top); left.lineTo(b.left, b.bottom - 4 * u); left.lineTo(mid, b.bottom); left.close();
        right.moveTo(mid, b.top + 6 * u); right.lineTo(b.right, b.top); right.lineTo(b.right, b.bottom - 4 * u); right.lineTo(mid, b.bottom); right.close();
        c.drawPath(left, fill);
        c.drawPath(right, fill);
        line.setColor(0xFF9AA0B4);
        line.setStrokeWidth(2.5f * u);
        for (int i = 1; i <= 3; i++) {
            float yy = b.top + b.height() * i / 4.5f;
            c.drawLine(b.left + 8 * u, yy, mid - 6 * u, yy + 3 * u, line);
            c.drawLine(mid + 6 * u, yy + 3 * u, b.right - 8 * u, yy, line);
        }
    }

    private void window(Canvas c, RectF r, int top, int bottom, float u) {
        float rad = 10 * u;
        fill.setShader(new LinearGradient(r.left, r.top, r.right, r.bottom, top, bottom, Shader.TileMode.CLAMP));
        c.drawRoundRect(r, rad, rad, fill);
        fill.setShader(null);
        line.setColor(BORDER);
        line.setStrokeWidth(3 * u);
        c.drawRoundRect(r, rad, rad, line);
        line.setColor(INNER);
        line.setStrokeWidth(1.5f * u);
        c.drawRoundRect(new RectF(r.left + 3 * u, r.top + 3 * u, r.right - 3 * u, r.bottom - 3 * u), rad - 3 * u, rad - 3 * u, line);
    }

    private void bar(Canvas c, float x, float y, float bw, float bh, double f, int color) {
        f = Math.max(0, Math.min(1, f));
        fill.setShader(null);
        fill.setColor(Color.argb(170, 0, 0, 0));
        c.drawRoundRect(x, y, x + bw, y + bh, bh / 2, bh / 2, fill);
        fill.setColor(color);
        if (f > 0) c.drawRoundRect(x, y, x + (float) (bw * f), y + bh, bh / 2, bh / 2, fill);
    }

    private void label(Canvas c, String s, float x, float y, float size, int color, Paint.Align a) {
        text.setTextSize(size);
        text.setColor(color);
        text.setTextAlign(a);
        text.setShadowLayer(size * 0.06f, size * 0.05f, size * 0.05f, 0xC0000000);
        c.drawText(s, x, y, text);
    }

    private float fit(String s, float maxW, float size) {
        text.setTextSize(size);
        float tw = text.measureText(s);
        return tw <= maxW ? size : size * maxW / tw;
    }

    private String ellipsize(String s, float maxW, float size) {
        text.setTextSize(size);
        if (text.measureText(s) <= maxW) return s;
        while (s.length() > 1 && text.measureText(s + "…") > maxW) s = s.substring(0, s.length() - 1);
        return s + "…";
    }

    // ------------------------------------------------------------------ touch

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        if (state.optInt("ready") != 1) { pressed = null; return true; }
        float x = e.getX(), y = e.getY();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_MOVE: {
                Button hit = null;
                for (Button b : buttons) if (b.box.contains(x, y)) hit = b;
                boolean book = bookBox.contains(x, y);
                if (hit != pressed || book != bookPressed) {
                    if ((hit != null && hit != pressed) || (book && !bookPressed)) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                    pressed = hit;
                    bookPressed = book;
                    invalidate();
                }
                return true;
            }
            case MotionEvent.ACTION_UP: {
                Button b = pressed;
                boolean book = bookPressed;
                pressed = null;
                bookPressed = false;
                invalidate();
                if (book && bookBox.contains(x, y)) GameActivity.toggleGuideFromPanel();
                if (b != null && b.box.contains(x, y)) run(b.cmd);
                return true;
            }
            case MotionEvent.ACTION_CANCEL:
                pressed = null;
                bookPressed = false;
                invalidate();
                return true;
        }
        return true;
    }

    private void run(int cmd) {
        switch (cmd) {
            case CMD_MENU: GameActivity.tapFromPanel(GameActivity.Y); return;
            case CMD_SAVE: GameActivity.keepStateFromPanel(this::note); return;
            case CMD_LOAD: GameActivity.loadLatestFromPanel(this::note); return;
            case CMD_SPEED: note(state.optInt("speed") == 1 ? "Normal speed" : "Speed " + speedFactor() + "×"); break;
            case CMD_BATTLES: note(state.optInt("noenc") == 1 ? "Random battles on" : "No random battles"); break;
            case CMD_BPLUS: note(state.optInt("bplus") == 1 ? "Battle assistance off" : "Battle assistance on"); break;
        }
        GameActivity.commandFromPanel(cmd);
    }

    void note(String s) {
        flash = s;
        flashUntil = android.os.SystemClock.uptimeMillis() + 1800;
        invalidate();
    }
}
