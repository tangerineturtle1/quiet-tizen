import re
import sqlite3
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
ROWS = [
    ("com.samsung.tv.quicklaunch", "XF86More", "package:com.samsung.tv.quicklaunch", "__K_SPLASH_SCREEN:LA_UI_VR_TRANSPARENT"),
    # The S90D has a second XF86More rule for function-osd; the bridge must leave it alone.
    ("com.samsung.tv.quicklaunch", "XF86More", "package:org.tizen.function-osd", "__K_BG_LAUNCH:enable __K_EX_FORCE_BOOST:enable"),
    ("org.tizen.menu", "XF86SysMenu", "package:org.tizen.menu", "uri:/opt/apps/org.tizen.menu/res/json/ATSC/menu.json"),
    ("unrelated.app", "Other", "package:unrelated.app", "unrelated"),
]


class S90dRoutingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        self.conn = sqlite3.connect(self.directory / "live.db")
        self.conn.execute("CREATE TABLE key_filter(appid TEXT, key TEXT, target_selector TEXT, args TEXT)")
        self.conn.executemany("INSERT INTO key_filter VALUES (?,?,?,?)", ROWS)
        self.conn.commit()
        with sqlite3.connect(self.directory / ".app-context.db") as snapshot:
            self.conn.backup(snapshot)

    def tearDown(self):
        self.conn.close()
        self.temp.cleanup()

    def sql(self, recipe):
        script = (ROOT / "recipes/s90d" / recipe).read_text()
        sql = re.search(r"<<SQL\n(.*?)\nSQL\n", script, re.S).group(1)
        for name, value in re.findall(r'^(SEL|NEW)="(.*)"$', script, re.M):
            sql = sql.replace("$" + name, value)
        return sql.replace("$D", str(self.directory))

    def rows(self):
        return self.conn.execute("SELECT * FROM key_filter ORDER BY rowid").fetchall()

    def test_install_reroutes_only_the_two_rules(self):
        self.conn.executescript(self.sql("install-settings-bridge-s90d.sh"))
        rows = self.rows()
        self.assertEqual(rows[0][2:], ("package:com.samsung.tv.bixby-promotion",
                                       "quiet_page:settings __K_SPLASH_SCREEN:LA_UI_VR_TRANSPARENT"))
        self.assertEqual(rows[2][2], "package:com.samsung.tv.bixby-promotion")
        self.assertEqual(rows[1], ROWS[1])
        self.assertEqual(rows[3], ROWS[3])

    def test_round_trip_restores_exactly(self):
        self.conn.executescript(self.sql("install-settings-bridge-s90d.sh"))
        self.conn.executescript(self.sql("restore-settings-bridge-s90d.sh"))
        self.assertEqual(self.rows(), ROWS)

    def test_restore_refuses_drift(self):
        self.conn.executescript(self.sql("install-settings-bridge-s90d.sh"))
        self.conn.execute("UPDATE key_filter SET args='changed by an update' WHERE key='XF86SysMenu'")
        self.conn.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.conn.executescript(self.sql("restore-settings-bridge-s90d.sh"))
        self.conn.rollback()
        self.assertEqual(self.rows()[2][3], "changed by an update")

    def test_install_refuses_unexpected_layout(self):
        self.conn.execute("DELETE FROM key_filter WHERE key='XF86SysMenu'")
        self.conn.commit()
        with self.assertRaises(sqlite3.IntegrityError):
            self.conn.executescript(self.sql("install-settings-bridge-s90d.sh"))


if __name__ == "__main__":
    unittest.main()
