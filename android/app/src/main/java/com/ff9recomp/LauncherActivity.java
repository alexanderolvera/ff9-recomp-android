package com.ff9recomp;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * The entry point: imports the player's FINAL FANTASY IX folder (with Memoria installed, and
 * Moguri Mod if they use it) once, then starts the game. Controller-friendly: the buttons take
 * focus and A presses them.
 */
public class LauncherActivity extends Activity {
    private static final int PICK_GAME = 1;
    private TextView status;
    private Button play, importBtn;
    private GameInstall install;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        install = new GameInstall(this);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        root.setBackgroundColor(Color.rgb(12, 14, 24));
        TextView title = new TextView(this);
        title.setText("FF9Recomp");
        title.setTextSize(34);
        title.setTextColor(Color.WHITE);
        title.setGravity(Gravity.CENTER);
        status = new TextView(this);
        status.setTextSize(16);
        status.setTextColor(Color.LTGRAY);
        status.setGravity(Gravity.CENTER);
        status.setPadding(40, 30, 40, 30);
        play = new Button(this);
        play.setText("Play");
        play.setOnClickListener(v -> startActivity(new Intent(this, GameActivity.class)));
        importBtn = new Button(this);
        importBtn.setText("Import game folder");
        importBtn.setOnClickListener(v -> startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE), PICK_GAME));
        root.addView(title);
        root.addView(status);
        root.addView(play);
        root.addView(importBtn);
        setContentView(root);
        refresh();
    }

    private void refresh() {
        boolean has = install.hasGame();
        String missing = has ? install.missingMessage() : null;
        if (!has) status.setText("Pick your FINAL FANTASY IX folder (Steam, with Memoria installed). It is copied into the app once.");
        else if (missing != null) status.setText(missing);
        else status.setText("Ready." + (install.hasMoguri() ? " Moguri Mod found." : ""));
        play.setEnabled(has && missing == null);
        (play.isEnabled() ? play : importBtn).requestFocus();
    }

    @Override
    protected void onActivityResult(int req, int res, Intent data) {
        super.onActivityResult(req, res, data);
        if (req != PICK_GAME || res != RESULT_OK || data == null) return;
        Uri tree = data.getData();
        play.setEnabled(false);
        importBtn.setEnabled(false);
        new Thread(() -> {
            String err = null;
            try {
                install.importFrom(tree, what -> runOnUiThread(() -> status.setText(what)));
            } catch (Exception e) {
                err = e.getMessage();
            }
            String e2 = err;
            runOnUiThread(() -> {
                importBtn.setEnabled(true);
                refresh();
                if (e2 != null) status.setText("Import failed: " + e2);
            });
        }).start();
    }

    @Override
    public boolean onKeyDown(int code, KeyEvent e) {
        if (code == KeyEvent.KEYCODE_BUTTON_A) {
            View f = getCurrentFocus();
            if (f != null) { f.performClick(); return true; }
        }
        return super.onKeyDown(code, e);
    }
}
