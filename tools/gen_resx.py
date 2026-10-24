import sys
from xml.sax.saxutils import escape

S = [
 # key, en, ja
 ("App_Name", "Window Rescue", "Window Rescue"),
 ("Menu_Title", "Window Rescue", "ウインドウ救出"),
 ("Menu_RescueHere", "Rescue All Windows to This Display", "この画面へ全ウインドウを救出"),
 ("Menu_OpenList", "Window List", "ウインドウ一覧"),
 ("Menu_OpenSettings", "Settings", "設定"),
 ("Menu_ShowMini", "Show Mini Window", "ミニウインドウを表示"),
 ("Mini_ThisDisplay", "This display: display {0}", "この画面：画面 {0}"),
 ("Mini_PinNew", "Show new windows on this display", "新しい窓をこの画面に出す"),
 ("Mini_FixTiny", "Fix very small windows", "小さい窓を補正する"),
 ("Mini_Gather", "Gather Windows to This Display", "窓をこの画面に集める"),
 ("Tiny_Fixed", "Fixed very small windows", "小さい窓を補正しました"),
 ("Tiny_None", "There are no very small windows to fix.", "補正が必要な小さい窓はありません。"),
 ("Menu_OpenPanel", "Rescue Panel", "救出パネル"),
 ("Menu_PinNewWindows", "Show New Windows on This Display (Display {0})", "新しい窓をこの画面（画面 {0}）に表示"),
 ("Pin_On", "New windows will open on display {0}", "新しい窓を画面 {0} に表示します"),
 ("Pin_Off", "New windows are no longer moved to a fixed display", "新しい窓の表示先固定を解除しました"),
 ("Action_RescueHere", "Rescue all windows to the display under the pointer", "ポインターのある画面へ全ウインドウを救出"),
 ("Menu_RestartElevated", "Restart as Administrator", "管理者として再起動"),
 ("Menu_About", "About", "バージョン情報"),
 ("About_Title", "About Window Rescue", "バージョン情報"),
 ("About_Version", "Version: ", "バージョン："),
 ("About_Company", "Developer: ", "開発元："),
 ("About_Libraries", "Libraries", "使用ライブラリ"),
 ("About_License", "App license", "アプリのライセンス"),
 ("About_ColName", "Name", "名前"),
 ("About_ColVersion", "Version", "バージョン"),
 ("About_ColLicense", "License", "ライセンス"),
 ("About_ColPurpose", "Used for", "用途"),
 ("About_Close", "Close", "閉じる"),
 ("About_PurposeApp", "The app itself", "アプリ本体"),
 ("About_DadsTokens", "DADS design tokens", "DADS デザイントークン"),
 ("About_PurposeDads", "Screen colors (© Digital Agency, Japan)", "画面の配色（© デジタル庁）"),
 ("About_PurposeRuntime", "Runtime", "アプリの実行環境"),
 ("About_NoLicense", "No license text is included.", "ライセンス本文は含まれていません。"),
 ("About_LinkError", "Could not open the browser.", "ブラウザーを起動できませんでした。"),
 ("Settings_ShowAbout", "Show Version Information", "バージョン情報を表示"),
 ("Menu_Exit", "Exit", "終了"),

 ("Common_Destination", "Destination Display", "救出先"),
 ("Common_On", "On", "オン"),
 ("Common_Off", "Off", "オフ"),
 ("Common_Remove", "Remove", "削除"),

 ("Panel_WindowTitle", "Rescue Panel - Window Rescue", "救出パネル - Window Rescue"),
 ("Panel_Heading", "Rescue Panel", "救出パネル"),
 ("Panel_Description", "Bring windows that are off-screen, on a display you cannot see, or too small to use back to the destination display.", "画面外や見えない画面にある窓、小さすぎて操作できない窓を救出先へ集めます。"),
 ("Panel_RescueAll", "Rescue All Windows to Destination", "救出先へ全ウインドウを救出"),
 ("Panel_ThisDisplayIs", "\"This display\" is display {0}, where this panel was opened.", "「この画面」は、このパネルを開いた画面 {0} です。"),
 ("Panel_LastResult", "Last result", "直前の結果"),

 ("Display_Label", "Display {0}: {1} ({2} × {3}, {4}%, Windows DISPLAY{5})", "画面 {0}：{1}（{2} × {3}、{4}%、Windows の DISPLAY{5}）"),

 ("List_WindowTitle", "Window List - Window Rescue", "ウインドウ一覧 - Window Rescue"),
 ("List_RescueSelected", "Rescue Selected Windows", "選択した窓を救出"),
 ("List_Refresh", "Refresh", "最新の状態にする"),
 ("List_Search", "Search by app, process, or title", "アプリ名・プロセス名・タイトルで検索"),
 ("List_ProblemsOnly", "Show only windows with problems", "問題のある窓だけ表示"),
 ("List_TableName", "Target windows", "対象ウインドウ"),
 ("List_ColApp", "App", "アプリ名"),
 ("List_ColProcess", "Process", "プロセス名"),
 ("List_ColTitle", "Window title", "ウインドウタイトル"),
 ("List_ColLocation", "Location", "所在"),
 ("List_ColPosition", "Position", "座標"),
 ("List_ColSize", "Size", "幅 × 高さ"),
 ("List_ColState", "State", "状態"),
 ("List_ColIssues", "Issues", "問題"),
 ("List_Empty", "No windows match.", "該当する窓はありません。"),
 ("List_NoTitle", "(No title)", "（タイトルなし）"),
 ("List_IssueSeparator", ", ", "、"),
 ("List_Count_One", "{0} window shown ({1} with problems in total)", "{0} 件を表示（問題のある窓は全体で {1} 件）"),
 ("List_Count_Other", "{0} windows shown ({1} with problems in total)", "{0} 件を表示（問題のある窓は全体で {1} 件）"),
 ("List_NoDestination", "Choose a destination display.", "救出先を選んでください。"),
 ("List_SelectFirst", "Select the windows to rescue.", "救出する窓を選んでください。"),

 ("State_Normal", "Normal", "通常"),
 ("State_Minimized", "Minimized", "最小化"),
 ("State_Maximized", "Maximized", "最大化"),
 ("Location_Offscreen", "Off-screen", "画面外"),
 ("Location_Display", "Display {0}", "画面 {0}"),
 ("Issue_NotResponding", "Not responding", "応答なし"),
 ("Issue_Tiny", "Very small", "極小"),
 ("Issue_PartiallyOffscreen", "Partly off-screen", "一部が画面外"),
 ("Issue_NotOperable", "Cannot be operated", "操作不可"),

 ("Status_Success", "Success", "成功"),
 ("Status_Warning", "Warning", "注意"),
 ("Status_Error", "Error", "エラー"),
 ("Status_Info", "Information", "お知らせ"),

 ("Result_RescuedTo", "Rescued windows to display {0}", "画面 {0} へ救出しました"),
 ("Result_Summary", "Moved: {0} · Skipped: {1} · Failed: {2}", "移動：{0} 件　対象外：{1} 件　失敗：{2} 件"),

 ("Reason_Excluded", "Excluded in settings", "設定で除外しています"),
 ("Reason_NotResponding", "The app is not responding", "アプリが応答していません"),
 ("Reason_Unexpected", "An unexpected error occurred", "予期しないエラーが発生しました"),
 ("Reason_Closed", "The window has been closed", "窓が閉じられています"),
 ("Reason_MoveRefused", "The app refused to move or resize", "アプリが移動・サイズ変更を受け付けませんでした"),
 ("Reason_AccessDenied", "Cannot move a window of an app running as administrator. Restart Window Rescue as administrator to move it.", "管理者権限で動くアプリの窓は移動できません。本アプリを管理者として再起動すると移動できます。"),
 ("Reason_DoesNotFit", "Did not fit completely; the title bar was placed in view", "完全には収まらないため、タイトルバーを見える位置に置きました"),

 ("Error_SaveSettings", "Could not save settings", "設定を保存できませんでした"),
 ("Error_Unexpected", "An unexpected error occurred", "予期しないエラーが発生しました"),
 ("Error_DisplayNotConnected", "Display {0} is not connected. No windows were moved.", "画面 {0} は接続されていません。窓は移動していません。"),
 ("Error_NoActiveWindow", "There is no selected window to move.", "移動できる選択中の窓がありません。"),
 ("Error_ContextMenuLabels", "The display language was changed, but the desktop menu text could not be updated. Use \"Fix Registration\" in Settings.", "表示言語は変更しましたが、デスクトップメニューの表示を更新できませんでした。設定の「登録を修正する」を実行してください。"),
 ("Error_AutoStart", "Could not change the startup setting", "自動起動の設定を変更できませんでした"),
 ("Error_InvalidNumber", "Some values are not valid", "入力値が正しくありません"),
 ("Error_InvalidNumberHint", "Thresholds: 20–2000. Standard width: 200–8000. Standard height: 150–8000 (DIP). Values were not saved.", "極小判定は 20〜2000、標準サイズの幅は 200〜8000、高さは 150〜8000（DIP）の整数で入力してください。保存していません。"),

 ("Settings_WindowTitle", "Settings - Window Rescue", "設定 - Window Rescue"),
 ("Settings_Heading", "Settings", "設定"),
 ("Settings_QuickActions", "Rescue", "救出"),
 ("Settings_Monitoring", "Automatic rescue of new windows", "新しい窓の自動救出"),
 ("Mode_Off", "Off", "監視しない"),
 ("Mode_OffHelp", "New windows are not corrected. Manual rescue is still available.", "新しい窓を補正しません。手動の救出は使えます。"),
 ("Mode_Auto", "Automatic rescue", "自動救出"),
 ("Mode_AutoHelp", "Only windows that open off-screen, partly outside, or too small are corrected. Normal windows are not moved.", "画面外・はみ出し・極小で開いた窓だけを補正します。正常な窓は動かしません。"),
 ("Mode_Fixed", "Always open on the destination display", "表示先を固定"),
 ("Mode_FixedHelp", "New windows are moved to the destination display. You can also turn this on from the tray menu: \"Show New Windows on This Display\".", "新しい窓を救出先へ移します。トレイメニューの「新しい窓をこの画面に表示」でも切り替えられます。"),
 ("Settings_AutoResize", "Automatic size correction: {0}", "自動サイズ補正：{0}"),
 ("Settings_AutoResizeHelp", "Enlarges resizable windows that are very small. Fixed-size windows and dialogs are never enlarged.", "サイズ変更できる窓が極端に小さい場合に拡大します。サイズ固定の窓やダイアログは拡大しません。"),
 ("Settings_TinyWidth", "Very small: width under", "極小と判定する幅（未満）"),
 ("Settings_TinyHeight", "Very small: height under", "極小と判定する高さ（未満）"),
 ("Settings_StdWidth", "Standard rescue width", "標準救出サイズの幅"),
 ("Settings_StdHeight", "Standard rescue height", "標準救出サイズの高さ"),
 ("Settings_DipHelp", "Values are in DIP (1 DIP = 1 pixel at 100% scaling). The standard size is limited to 90% of the destination work area.", "単位は DIP（拡大率 100% での 1 ピクセル）です。標準救出サイズは救出先の作業領域の 90% までに制限します。"),
 ("Settings_Exclusions", "Excluded apps", "除外するアプリ"),
 ("Settings_ExclusionsHelp", "Apps listed here are not corrected automatically. Manual rescue still includes them.", "ここに登録したアプリは自動補正しません。手動の救出には含まれます。"),
 ("Settings_ProcessName", "Process name (e.g. notepad)", "プロセス名（例：notepad）"),
 ("Settings_ExclusionScope", "What to exclude", "除外する範囲"),
 ("Settings_AddExclusion", "Add", "追加"),
 ("Settings_NoExclusions", "No apps are excluded.", "除外しているアプリはありません。"),
 ("Exclusion_All", "All automatic correction", "自動補正すべて"),
 ("Exclusion_SizeOnly", "Size correction only", "サイズ補正だけ"),

 ("Settings_Shortcuts", "Keyboard shortcuts", "ショートカット"),
 ("Settings_ShortcutsHelp", "Select a field and press the new key combination. Use at least one of Ctrl, Alt, Shift, or Win. Press Delete to remove a shortcut.", "欄を選んで新しいキーの組み合わせを押します。Ctrl・Alt・Shift・Win のいずれかを含めてください。Delete キーで割り当てを外します。"),
 ("Settings_ResetShortcuts", "Restore Default Shortcuts", "初期設定に戻す"),
 ("Shortcut_None", "(None)", "（なし）"),
 ("Shortcut_CaptureHelp", "Press the new key combination. Press Delete to remove it.", "新しいキーの組み合わせを押してください。Delete キーで割り当てを外します。"),
 ("Shortcut_Registered", "Available", "使用できます"),
 ("Shortcut_Failed", "Cannot be used because another app is using this key", "他のアプリが使用中のため使えません"),
 ("Shortcut_Duplicate", "Already assigned to \"{0}\"", "「{0}」と重複しています"),
 ("Shortcut_Invalid", "Not a valid key combination", "キーの組み合わせが正しくありません"),
 ("Shortcut_Unassigned", "Not assigned", "割り当てなし"),
 ("Shortcut_Clear", "Remove", "外す"),
 ("Shortcut_NeedModifier", "Include Ctrl, Alt, Shift, or Win", "Ctrl・Alt・Shift・Win のいずれかを含めてください"),
 ("Action_RescueForeground", "Move selected window to display {0}", "選択中の窓を画面 {0} へ救出"),
 ("Action_RescueAll", "Rescue all windows to display {0}", "全ウインドウを画面 {0} へ救出"),
 ("Action_OpenList", "Open window list", "ウインドウ一覧を開く"),

 ("Settings_ContextMenu", "Desktop context menu", "右クリックメニュー"),
 ("Settings_ContextMenuHelp", "Adds \"Window Rescue\" to the menu shown when you right-click an empty area of the desktop. In Windows 11, select \"Show more options\" to see it. Only your user account is affected, and no administrator rights are needed.", "デスクトップの何もない場所を右クリックしたメニューに「ウインドウ救出」を追加します。Windows 11 では「その他のオプションを確認」を選ぶと表示されます。現在のユーザーだけに登録し、管理者権限は不要です。"),
 ("Settings_Register", "Add to Desktop Context Menu", "登録する"),
 ("Settings_RegisterRepair", "Fix Registration", "登録を修正する"),
 ("Settings_Unregister", "Remove from Desktop Context Menu", "登録を解除する"),
 ("ContextMenu_Failed", "The operation failed", "操作に失敗しました"),
 ("ContextMenu_CurrentState", "Current state: {0}", "現在の状態：{0}"),
 ("ContextMenu_NotRegistered", "Not added", "未登録"),
 ("ContextMenu_NotRegistered_Help", "The menu item is not added to the desktop context menu.", "デスクトップの右クリックメニューには追加されていません。"),
 ("ContextMenu_Registered", "Added", "登録済み"),
 ("ContextMenu_Registered_Help", "Right-click the desktop and select \"Show more options\" to use it.", "デスクトップを右クリックし「その他のオプションを確認」から使えます。"),
 ("ContextMenu_NeedsRepair", "Registration needs to be fixed", "登録先の修正が必要"),
 ("ContextMenu_NeedsRepair_Help", "The registered location does not match this app (for example, the app was moved). Select \"Fix Registration\".", "登録されている起動先がこのアプリと一致しません（アプリを移動した場合など）。「登録を修正する」を選んでください。"),

 ("Language_System", "Use Windows Display Language", "Windowsに合わせる"),

 ("Settings_Startup", "Startup and permissions", "起動と権限"),
 ("Settings_AutoStart", "Start automatically when I sign in to Windows", "Windows へのサインイン時に自動で起動する"),
 ("Settings_Privilege", "Permission", "権限"),
 ("Settings_RunningStandard", "Running with standard permissions. Windows of apps running as administrator may not be movable.", "標準の権限で動作しています。管理者権限で動くアプリの窓は移動できない場合があります。"),
 ("Settings_RunningElevated", "Running as administrator.", "管理者権限で動作しています。"),
 ("Settings_StoredAt", "Settings are stored only on this PC: {0}", "設定はこの PC 内だけに保存します：{0}"),
]

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'''

def write(path, idx):
    keys = set()
    with open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(HEADER)
        for row in S:
            k = row[0]
            assert k not in keys, k
            keys.add(k)
            f.write(f'  <data name="{k}" xml:space="preserve"><value>{escape(row[idx])}</value></data>\n')
        f.write("</root>\n")

out = sys.argv[1]
write(out + "/Strings.resx", 1)
write(out + "/Strings.ja.resx", 2)
print(len(S), "strings")
