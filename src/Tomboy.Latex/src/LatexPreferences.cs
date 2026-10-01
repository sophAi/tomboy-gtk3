using System;
using Gtk;

namespace Tomboy.Latex
{
    public class LatexPreferencesWidget : Box
    {
        private readonly TextView textHeader;
        private readonly TextView textFooter;
        private readonly CheckButton dollarCheckbutton;
        private readonly Button resetButton;
        private readonly Button applyButton;

        public event Action<string, string, bool>? SettingsApplied;

        public LatexPreferencesWidget(string initialHeader, string initialFooter, bool initialDollarEnabled)
            : base(Orientation.Vertical, 10)
        {
            BorderWidth = 8;

            bool isAvailable = LatexManager.IsLatexAvailable();
            var statusBox = new Box(Orientation.Horizontal, 6);
            if (isAvailable)
            {
                var okIcon = new Image(Stock.Apply, IconSize.Menu);
                var okLabel = new Label("系統已安裝 LaTeX 與 dvipng 工具，可正常即時繪製數學公式。") { Xalign = 0 };
                statusBox.PackStart(okIcon, false, false, 0);
                statusBox.PackStart(okLabel, false, false, 0);
            }
            else
            {
                var warnIcon = new Image(Stock.DialogWarning, IconSize.Menu);
                var warnLabel = new Label("未偵測到 latex 或 dvipng 指令。請在終端機安裝：sudo apt install texlive-latex-base dvipng") { Xalign = 0 };
                statusBox.PackStart(warnIcon, false, false, 0);
                statusBox.PackStart(warnLabel, false, false, 0);
            }
            PackStart(statusBox, false, false, 0);

            var descLabel = new Label(
                "在筆記中輸入以 \\[ 與 \\] 包裹的數學公式（例如：\\[E = mc^2\\]），即可即時顯示 LaTeX 算式影像。\n" +
                "點選公式或將游標移入時會自動展開原始碼方便編輯；移開游標即重新渲染。"
            )
            {
                Wrap = true,
                Xalign = 0
            };
            PackStart(descLabel, false, false, 0);

            var headerLabel = new Label("LaTeX 檔案開頭範本 (Header)：") { Xalign = 0 };
            PackStart(headerLabel, false, false, 0);

            var headerScroll = new ScrolledWindow { ShadowType = ShadowType.In, HeightRequest = 90 };
            textHeader = new TextView { WrapMode = WrapMode.Word };
            textHeader.Buffer.Text = string.IsNullOrEmpty(initialHeader) ? LatexManager.DEFAULT_HEADER : initialHeader;
            textHeader.Buffer.Changed += (s, e) => { applyButton.Sensitive = true; resetButton.Sensitive = true; };
            headerScroll.Add(textHeader);
            PackStart(headerScroll, false, false, 0);

            var formulaPlaceholder = new Label("  [-- 筆記裡的數學公式內容插入於此 --]") { Xalign = 0 };
            PackStart(formulaPlaceholder, false, false, 0);

            var footerLabel = new Label("LaTeX 檔案結尾範本 (Footer)：") { Xalign = 0 };
            PackStart(footerLabel, false, false, 0);

            var footerScroll = new ScrolledWindow { ShadowType = ShadowType.In, HeightRequest = 60 };
            textFooter = new TextView { WrapMode = WrapMode.Word };
            textFooter.Buffer.Text = string.IsNullOrEmpty(initialFooter) ? LatexManager.DEFAULT_FOOTER : initialFooter;
            textFooter.Buffer.Changed += (s, e) => { applyButton.Sensitive = true; resetButton.Sensitive = true; };
            footerScroll.Add(textFooter);
            PackStart(footerScroll, false, false, 0);

            dollarCheckbutton = new CheckButton("同時支援以 $...$ 包裹的行內公式 (需重啟筆記視窗生效)")
            {
                Active = initialDollarEnabled
            };
            dollarCheckbutton.Toggled += (s, e) => { applyButton.Sensitive = true; resetButton.Sensitive = true; };
            PackStart(dollarCheckbutton, false, false, 4);

            var btnBox = new Box(Orientation.Horizontal, 8);
            resetButton = new Button("重設為預設值");
            resetButton.Clicked += OnResetClicked;

            applyButton = new Button("套用設定") { Sensitive = false };
            applyButton.Clicked += OnApplyClicked;

            btnBox.PackEnd(applyButton, false, false, 0);
            btnBox.PackEnd(resetButton, false, false, 0);
            PackStart(btnBox, false, false, 4);

            ShowAll();
        }

        private void OnResetClicked(object? sender, EventArgs e)
        {
            textHeader.Buffer.Text = LatexManager.DEFAULT_HEADER;
            textFooter.Buffer.Text = LatexManager.DEFAULT_FOOTER;
            dollarCheckbutton.Active = false;
            applyButton.Sensitive = true;
        }

        private void OnApplyClicked(object? sender, EventArgs e)
        {
            string header = textHeader.Buffer.Text.Trim();
            string footer = textFooter.Buffer.Text.Trim();
            bool dollar = dollarCheckbutton.Active;

            SettingsApplied?.Invoke(header, footer, dollar);
            applyButton.Sensitive = false;
        }
    }
}
