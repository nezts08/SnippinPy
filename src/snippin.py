from __future__ import annotations

import json
import re
import time
from datetime import datetime, timezone
from pathlib import Path
import tkinter as tk
from tkinter import ttk, messagebox

try:
    import keyboard
    import pyperclip
    import win32gui
    import win32process
    import os
except ImportError as exc:
    raise SystemExit(
        "Faltan dependencias. Ejecuta: pip install -r requerimientos.txt\n"
        f"Detalle: {exc}"
    )

APP_DIR = Path(__file__).resolve().parent
CONFIG_FILE = APP_DIR / "command.json"
# Paleta oscura con acentos violeta
BG = "#0b0b14"
PANEL = "#141421"
PANEL_ALT = "#191827"
INPUT = "#201e30"
INPUT_HOVER = "#29253d"
FG = "#f4f1ff"
MUTED = "#a7a1bd"
PURPLE = "#8b5cf6"
PURPLE_LIGHT = "#a78bfa"
PURPLE_DARK = "#6336c9"
BORDER = "#302a46"
RED = "#ef476f"
RED_DARK = "#c9365a"
GREEN = "#43c59e"

DEFAULT_CONFIG = {
    "trigger_key": 9,
    "trigger_key_name": "TAB",
    "trigger_modifiers": 0,
    "snippet_dates": {},
    "commands": {"ejemplo": "Texto de prueba"},
}

MODIFIER_NAMES = {"ctrl": "ctrl", "alt": "alt", "shift": "shift", "win": "windows"}


def utc_now() -> str:
    return (
        datetime.now(timezone.utc)
        .isoformat(timespec="milliseconds")
        .replace("+00:00", "Z")
    )


def parse_date(value: str | None) -> datetime:
    if not value:
        return datetime.min.replace(tzinfo=timezone.utc)
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except (ValueError, TypeError):
        return datetime.min.replace(tzinfo=timezone.utc)


class SnippinApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.config = self.load_config()
        self.buffer = ""
        self.editing_key: str | None = None
        self.refreshing = False
        self.hotkey_handle: str | None = None
        self.keyboard_hook = None
        self.recorded_shortcut: str | None = None

        self.build_ui()
        self.refresh_list()
        self.install_keyboard_hook()
        self.root.protocol("WM_DELETE_WINDOW", self.close)

    def load_config(self) -> dict:
        if CONFIG_FILE.exists():
            try:
                data = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
                if not isinstance(data, dict):
                    raise ValueError("Configuración inválida")
                data.setdefault("trigger_key", 9)
                data.setdefault("trigger_key_name", "TAB")
                data.setdefault("trigger_modifiers", 0)
                data.setdefault("commands", {})
                data.setdefault("snippet_dates", {})
                for key in data["commands"]:
                    data["snippet_dates"].setdefault(
                        key, {"created_at": None, "modified_at": None}
                    )
                return data
            except (OSError, ValueError, json.JSONDecodeError):
                messagebox.showwarning(
                    "Snippin",
                    "No se pudo leer command.json. Se creará una configuración nueva.",
                )
        return json.loads(json.dumps(DEFAULT_CONFIG))

    def save_config(self) -> None:
        try:
            CONFIG_FILE.write_text(
                json.dumps(self.config, ensure_ascii=False, indent=2), encoding="utf-8"
            )
        except OSError as exc:
            messagebox.showerror(
                "Snippin", f"Error al guardar la configuración:\n{exc}"
            )

    def build_ui(self) -> None:
        self.root.title("Snippin")
        self.root.geometry("1180x720")
        self.root.minsize(960, 600)
        self.root.configure(bg=BG)

        style = ttk.Style()
        style.theme_use("clam")
        style.configure(
            "Dark.TCombobox",
            fieldbackground=INPUT,
            background=INPUT,
            foreground=FG,
            arrowcolor=PURPLE_LIGHT,
            bordercolor=BORDER,
            lightcolor=INPUT,
            darkcolor=INPUT,
            padding=(12, 10),
            relief="flat",
        )
        style.map(
            "Dark.TCombobox",
            fieldbackground=[("readonly", INPUT), ("active", INPUT_HOVER)],
            foreground=[("readonly", FG)],
            selectbackground=[("readonly", INPUT)],
            selectforeground=[("readonly", FG)],
            bordercolor=[("focus", PURPLE)],
        )

        # Estructura principal
        shell = tk.Frame(self.root, bg=BG)
        shell.pack(fill="both", expand=True, padx=18, pady=18)

        # Barra superior / identidad
        topbar = tk.Frame(shell, bg=BG, height=62)
        topbar.pack(fill="x", pady=(0, 16))
        topbar.pack_propagate(False)

        brand = tk.Frame(topbar, bg=BG)
        brand.pack(side="left", fill="y")
        logo = tk.Label(
            brand,
            text="S",
            bg=PURPLE_DARK,
            fg="white",
            font=("Segoe UI", 17, "bold"),
            width=2,
            height=1,
        )
        logo.pack(side="left", padx=(0, 12), pady=7)
        brand_text = tk.Frame(brand, bg=BG)
        brand_text.pack(side="left", pady=7)
        tk.Label(
            brand_text,
            text="Snippin",
            bg=BG,
            fg=FG,
            font=("Segoe UI", 17, "bold"),
        ).pack(anchor="w")
        tk.Label(
            brand_text,
            text="GESTOR DE FRAGMENTOS DE TEXTO",
            bg=BG,
            fg=MUTED,
            font=("Segoe UI", 8, "bold"),
        ).pack(anchor="w", pady=(1, 0))

        self.make_button(
            topbar,
            "⚙  Configuración",
            self.show_settings,
            color=PANEL_ALT,
        ).pack(side="right", pady=9)

        # Paneles
        content = tk.Frame(shell, bg=BG)
        content.pack(fill="both", expand=True)

        left = tk.Frame(
            content,
            bg=PANEL,
            width=310,
            highlightthickness=1,
            highlightbackground=BORDER,
        )
        left.pack(side="left", fill="y", padx=(0, 14))
        left.pack_propagate(False)

        sidebar_header = tk.Frame(left, bg=PANEL)
        sidebar_header.pack(fill="x", padx=17, pady=(17, 12))
        tk.Label(
            sidebar_header,
            text="Tus snippets",
            bg=PANEL,
            fg=FG,
            font=("Segoe UI", 13, "bold"),
        ).pack(side="left")
        self.make_button(
            sidebar_header,
            "+ Nuevo",
            self.new_snippet,
            color=PURPLE_DARK,
        ).pack(side="right")

        search_wrap = tk.Frame(
            left, bg=INPUT, highlightthickness=1, highlightbackground=BORDER
        )
        search_wrap.pack(fill="x", padx=14, pady=(0, 10))
        tk.Label(
            search_wrap,
            text="⌕",
            bg=INPUT,
            fg=PURPLE_LIGHT,
            font=("Segoe UI", 17, "bold"),
        ).pack(side="left", padx=(10, 4))
        self.search_var = tk.StringVar()
        search = tk.Entry(
            search_wrap,
            textvariable=self.search_var,
            bg=INPUT,
            fg=FG,
            insertbackground=PURPLE_LIGHT,
            relief="flat",
            font=("Segoe UI", 10),
            borderwidth=0,
        )
        search.pack(side="left", fill="x", expand=True, ipady=10, padx=(0, 10))
        search.bind("<KeyRelease>", lambda _event: self.refresh_list())

        sort_wrap = tk.Frame(left, bg=PANEL)
        sort_wrap.pack(fill="x", padx=14, pady=(0, 12))
        tk.Label(
            sort_wrap,
            text="ORDENAR POR",
            bg=PANEL,
            fg=MUTED,
            font=("Segoe UI", 8, "bold"),
        ).pack(anchor="w", pady=(0, 5))
        self.sort_var = tk.StringVar(value="Nombre (A-Z)")
        sort = ttk.Combobox(
            sort_wrap,
            textvariable=self.sort_var,
            state="readonly",
            values=[
                "Nombre (A-Z)",
                "Nombre (Z-A)",
                "Recién creado",
                "Más antiguo",
                "Recién modificado",
            ],
            style="Dark.TCombobox",
        )
        sort.pack(fill="x")
        sort.bind("<<ComboboxSelected>>", lambda _event: self.refresh_list())

        list_area = tk.Frame(left, bg=PANEL)
        list_area.pack(fill="both", expand=True, padx=9, pady=(0, 10))
        self.listbox = tk.Listbox(
            list_area,
            bg=PANEL,
            fg=FG,
            selectbackground=PURPLE_DARK,
            selectforeground="white",
            relief="flat",
            bd=0,
            font=("Segoe UI", 10),
            activestyle="none",
            highlightthickness=0,
            selectborderwidth=0,
            exportselection=False,
        )
        self.listbox.pack(fill="both", expand=True, padx=3)
        self.listbox.bind("<<ListboxSelect>>", self.select_snippet)

        # Editor
        right = tk.Frame(
            content,
            bg=PANEL,
            highlightthickness=1,
            highlightbackground=BORDER,
        )
        right.pack(side="left", fill="both", expand=True)

        editor_head = tk.Frame(right, bg=PANEL)
        editor_head.pack(fill="x", padx=25, pady=(22, 18))
        self.title_label = tk.Label(
            editor_head,
            text="Nuevo snippet",
            bg=PANEL,
            fg=FG,
            font=("Segoe UI", 18, "bold"),
            anchor="w",
        )
        self.title_label.pack(anchor="w")
        tk.Label(
            editor_head,
            text="Crea y administra tus expansiones de texto.",
            bg=PANEL,
            fg=MUTED,
            font=("Segoe UI", 9),
        ).pack(anchor="w", pady=(4, 0))

        editor_body = tk.Frame(right, bg=PANEL)
        editor_body.pack(fill="both", expand=True, padx=25, pady=(0, 12))

        tk.Label(
            editor_body,
            text="COMANDO",
            bg=PANEL,
            fg=PURPLE_LIGHT,
            font=("Segoe UI", 8, "bold"),
        ).pack(anchor="w", pady=(0, 7))
        key_wrap = tk.Frame(
            editor_body,
            bg=INPUT,
            highlightthickness=1,
            highlightbackground=BORDER,
        )
        key_wrap.pack(fill="x", pady=(0, 20))
        self.key_entry = tk.Entry(
            key_wrap,
            bg=INPUT,
            fg=FG,
            insertbackground=PURPLE_LIGHT,
            relief="flat",
            font=("Consolas", 11),
            borderwidth=0,
        )
        self.key_entry.pack(fill="x", ipady=11, padx=12)

        tk.Label(
            editor_body,
            text="TEXTO EXPANDIDO",
            bg=PANEL,
            fg=PURPLE_LIGHT,
            font=("Segoe UI", 8, "bold"),
        ).pack(anchor="w", pady=(0, 7))

        text_wrap = tk.Frame(
            editor_body,
            bg=INPUT,
            highlightthickness=1,
            highlightbackground=BORDER,
        )
        text_wrap.pack(fill="both", expand=True)
        self.value_text = tk.Text(
            text_wrap,
            bg=INPUT,
            fg=FG,
            insertbackground=PURPLE_LIGHT,
            relief="flat",
            wrap="word",
            font=("Consolas", 11),
            undo=True,
            padx=14,
            pady=13,
            spacing1=3,
            selectbackground=PURPLE_DARK,
        )
        self.value_text.pack(fill="both", expand=True)

        actions = tk.Frame(right, bg=PANEL)
        actions.pack(fill="x", padx=25, pady=(3, 22))
        self.make_button(
            actions,
            "Guardar cambios",
            self.save_snippet,
            color=PURPLE_DARK,
        ).pack(side="left", padx=(0, 9))
        self.delete_button = self.make_button(
            actions,
            "Eliminar",
            self.delete_snippet,
            color=RED_DARK,
        )
        self.delete_button.pack(side="left")
        self.delete_button.pack_forget()

        footer = tk.Label(
            shell,
            text="  ●  Snippin está activo     ·     Al escribir un espacio, Enter o Tab se reinicia la captura del texto anterior.",
            bg=BG,
            fg=MUTED,
            font=("Segoe UI", 8),
            anchor="w",
        )
        footer.pack(side="bottom", fill="x", pady=(12, 0))

    @staticmethod
    def make_button(
        parent: tk.Widget,
        label: str,
        command,
        color: str = INPUT,
        width: int | None = None,
    ) -> tk.Button:
        is_danger = color in (RED, RED_DARK)
        hover_color = (
            RED
            if is_danger
            else (PURPLE if color in (PURPLE_DARK, PURPLE) else INPUT_HOVER)
        )
        button = tk.Button(
            parent,
            text=label,
            command=command,
            bg=color,
            fg="white",
            activebackground=hover_color,
            activeforeground="white",
            relief="flat",
            bd=0,
            cursor="hand2",
            font=("Segoe UI", 9, "bold"),
            padx=15,
            pady=9,
            width=width,
            highlightthickness=0,
        )
        button.bind("<Enter>", lambda _event: button.configure(bg=hover_color))
        button.bind("<Leave>", lambda _event: button.configure(bg=color))
        return button

    def refresh_list(self) -> None:
        if not hasattr(self, "listbox"):
            return
        selected = self.editing_key
        query = self.search_var.get().strip().casefold()
        commands = self.config["commands"]
        keys = [
            key
            for key, value in commands.items()
            if query in key.casefold() or query in value.casefold()
        ]
        dates = self.config["snippet_dates"]
        sort_mode = self.sort_var.get()
        if sort_mode == "Nombre (Z-A)":
            keys.sort(key=str.casefold, reverse=True)
        elif sort_mode == "Recién creado":
            keys.sort(
                key=lambda key: parse_date(dates.get(key, {}).get("created_at")),
                reverse=True,
            )
        elif sort_mode == "Más antiguo":
            keys.sort(key=lambda key: parse_date(dates.get(key, {}).get("created_at")))
        elif sort_mode == "Recién modificado":
            keys.sort(
                key=lambda key: parse_date(dates.get(key, {}).get("modified_at")),
                reverse=True,
            )
        else:
            keys.sort(key=str.casefold)
        self.refreshing = True
        self.listbox.delete(0, "end")
        for key in keys:
            self.listbox.insert("end", key)
        if selected in keys:
            index = keys.index(selected)
            self.listbox.selection_set(index)
            self.listbox.activate(index)
        self.refreshing = False

    def select_snippet(self, _event=None) -> None:
        if self.refreshing or not self.listbox.curselection():
            return
        key = str(self.listbox.get(self.listbox.curselection()[0]))
        self.editing_key = key
        self.key_entry.delete(0, "end")
        self.key_entry.insert(0, key)
        self.key_entry.configure(state="disabled")
        self.value_text.delete("1.0", "end")
        self.value_text.insert("1.0", self.config["commands"][key])
        self.title_label.configure(text="Editando Snippet")
        self.delete_button.pack(side="left")

    def new_snippet(self) -> None:
        self.editing_key = None
        self.listbox.selection_clear(0, "end")
        self.key_entry.configure(state="normal")
        self.key_entry.delete(0, "end")
        self.value_text.delete("1.0", "end")
        self.title_label.configure(text="Nuevo Snippet")
        self.delete_button.pack_forget()
        self.key_entry.focus_set()

    def save_snippet(self) -> None:
        key = self.key_entry.get().strip().lower()
        value = self.value_text.get("1.0", "end-1c")
        if not key or not value.strip():
            messagebox.showwarning(
                "Snippin", "Completa el comando y el texto expandido."
            )
            return
        if not re.fullmatch(r"[a-z0-9]+", key):
            messagebox.showwarning(
                "Snippin", "El comando solo puede contener letras y números."
            )
            return
        if self.editing_key and key != self.editing_key:
            self.config["commands"].pop(self.editing_key, None)
            self.config["snippet_dates"].pop(self.editing_key, None)
        now = utc_now()
        dates = self.config["snippet_dates"].setdefault(key, {"created_at": now})
        dates.setdefault("created_at", now)
        dates["modified_at"] = now
        self.config["commands"][key] = value
        self.save_config()
        self.refresh_list()
        self.new_snippet()

    def delete_snippet(self) -> None:
        key = self.editing_key
        if not key:
            return
        if not messagebox.askyesno("Snippin", f"¿Eliminar el snippet '{key}'?"):
            return
        self.config["commands"].pop(key, None)
        self.config["snippet_dates"].pop(key, None)
        self.save_config()
        self.new_snippet()
        self.refresh_list()

    def show_settings(self) -> None:
        dialog = tk.Toplevel(self.root)
        dialog.title("Configuración de activación")
        dialog.geometry("460x290")
        dialog.resizable(False, False)
        dialog.configure(bg=BG)
        dialog.transient(self.root)
        dialog.grab_set()
        tk.Label(
            dialog,
            text="Pulsa 'Grabar' y presiona la combinación deseada:",
            bg=BG,
            fg=FG,
        ).pack(anchor="w", padx=20, pady=(22, 12))
        display_var = tk.StringVar(value=self.config.get("trigger_key_name", "TAB"))
        display = tk.Entry(
            dialog,
            textvariable=display_var,
            state="readonly",
            readonlybackground=INPUT,
            fg=FG,
            relief="flat",
            justify="center",
        )
        display.pack(fill="x", padx=20, ipady=8)
        state: dict[str, str | None] = {"shortcut": None}
        recording = {"active": False}

        def record() -> None:
            recording["active"] = True
            state["shortcut"] = None
            display_var.set("Presiona tu combinación... (Esc cancela)")
            dialog.focus_force()

        def capture(event: tk.Event) -> str | None:
            if not recording["active"]:
                return None
            if event.keysym == "Escape":
                recording["active"] = False
                display_var.set(self.config.get("trigger_key_name", "TAB"))
                return "break"
            modifiers: list[str] = []
            if event.state & 0x0004:
                modifiers.append("ctrl")
            if event.state & 0x0008:
                modifiers.append("alt")
            if event.state & 0x0001:
                modifiers.append("shift")
            if event.state & 0x20000:
                modifiers.append("windows")
            key = event.keysym
            if key in (
                "Control_L",
                "Control_R",
                "Alt_L",
                "Alt_R",
                "Shift_L",
                "Shift_R",
                "Meta_L",
                "Meta_R",
            ):
                return "break"
            if (
                not modifiers
                and key.lower() != "tab"
                and not re.fullmatch(r"F([1-9]|1[0-9]|2[0-4])", key, re.I)
            ):
                display_var.set("Usa Ctrl/Alt/Shift/Win o TAB/F1-F24")
                return "break"
            names = {"Prior": "page up", "Next": "page down", "space": "space"}
            key_name = names.get(key, key.lower())
            shortcut = "+".join([*modifiers, key_name])
            state["shortcut"] = shortcut
            display_var.set(
                " + ".join(
                    [m.title() for m in modifiers]
                    + [
                        (
                            key.upper()
                            if key.lower().startswith("f") or key.lower() == "tab"
                            else key
                        )
                    ]
                )
            )
            recording["active"] = False
            return "break"

        dialog.bind_all("<KeyPress>", capture, add="+")
        self.make_button(dialog, "Grabar combinación", record, color=PURPLE_DARK).pack(
            fill="x", padx=20, pady=14
        )

        def save() -> None:
            shortcut = state["shortcut"]
            if recording["active"]:
                return
            if not shortcut:
                messagebox.showinfo(
                    "Snippin", "No se ha grabado una combinación nueva.", parent=dialog
                )
                return
            self.config["trigger_key_name"] = shortcut.upper().replace("+", " + ")
            self.config["trigger_modifiers"] = sum(
                {"ctrl": 1, "alt": 2, "shift": 4, "windows": 8}.get(part, 0)
                for part in shortcut.split("+")[:-1]
            )
            self.config["trigger_key"] = shortcut.split("+")[-1]
            self.save_config()
            self.buffer = ""
            self.install_keyboard_hook()
            dialog.destroy()

        self.make_button(dialog, "Guardar cambios", save, color=PURPLE_DARK).pack(
            fill="x", padx=20, pady=(0, 12)
        )
        dialog.bind("<KeyPress>", capture, add="+")
        dialog.protocol("WM_DELETE_WINDOW", dialog.destroy)

    def is_own_window_active(self) -> bool:
        try:
            hwnd = win32gui.GetForegroundWindow()
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            return pid == os.getpid()
        except Exception:
            return False

    def install_keyboard_hook(self) -> None:
        try:
            if self.hotkey_handle is not None:
                keyboard.remove_hotkey(self.hotkey_handle)
                self.hotkey_handle = None
            if self.keyboard_hook is not None:
                keyboard.unhook(self.keyboard_hook)
            self.keyboard_hook = keyboard.hook(self.on_key_event, suppress=False)
            shortcut = (
                str(self.config.get("trigger_key_name", "TAB")).lower().replace(" ", "")
            )
            shortcut = shortcut.replace("win+", "windows+")
            self.hotkey_handle = keyboard.add_hotkey(
                shortcut, self.on_trigger, suppress=True, trigger_on_release=False
            )
        except Exception as exc:
            messagebox.showerror(
                "Snippin",
                "No se pudo activar el teclado global.\n"
                "Prueba ejecutar Python como administrador.\n\n" + str(exc),
            )

    def on_key_event(self, event) -> None:
        if event.event_type != "down":
            return
        if self.is_own_window_active():
            self.buffer = ""
            return
        name = event.name or ""
        if name in (
            "shift",
            "left shift",
            "right shift",
            "ctrl",
            "left ctrl",
            "right ctrl",
            "alt",
            "left alt",
            "right alt",
            "windows",
            "left windows",
            "right windows",
        ):
            return
        if name == "backspace":
            self.buffer = self.buffer[:-1]
        elif name in ("space", "enter", "tab"):
            self.buffer = ""
        elif len(name) == 1 and name.isascii() and name.isalnum():
            self.buffer = (self.buffer + name.lower())[-100:]
        else:
            self.buffer = ""

    def on_trigger(self) -> None:
        if self.is_own_window_active():
            self.buffer = ""
            return
        word = self.buffer.lower()
        self.buffer = ""
        expanded = self.config["commands"].get(word)
        if expanded is not None:
            self.root.after(0, lambda: self.expand_text(word, expanded))

    def expand_text(self, word: str, expanded: str) -> None:
        try:
            for _ in word:
                keyboard.send("backspace")
                time.sleep(0.005)
            pyperclip.copy(expanded)
            time.sleep(0.05)
            keyboard.send("ctrl+v")
        except Exception as exc:
            messagebox.showerror("Snippin", f"No se pudo expandir el texto:\n{exc}")

    def close(self) -> None:
        try:
            if self.hotkey_handle is not None:
                keyboard.remove_hotkey(self.hotkey_handle)
            if self.keyboard_hook is not None:
                keyboard.unhook(self.keyboard_hook)
            keyboard.unhook_all()
        except Exception:
            pass
        self.root.destroy()


def main() -> None:
    root = tk.Tk()
    SnippinApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
