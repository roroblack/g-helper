using GHelper.Mode;
using GHelper.UI;

namespace GHelper;
public partial class SettingsForm
{
    private RButton buttonWindows = new();
    private RButton buttonCustom = new();
    private ToolTip modeHelp = new();

        protected void VisualiseMode(int mode)
        {
            buttonSilent.Activated = false;
            buttonBalanced.Activated = false;
            buttonTurbo.Activated = false;
            buttonFans.Activated = false;
            buttonManual.Activated = false;
            buttonWindows.Activated = mode == Modes.WindowsSlot;
            buttonCustom.Activated = Modes.IsCustom(mode);

            switch (mode)
            {
                case Modes.ManualSlot:
                    buttonManual.Activated = true;
                    break;
                case AsusACPI.PerformanceSilent:
                    buttonSilent.Activated = true;
                    break;
                case AsusACPI.PerformanceTurbo:
                    buttonTurbo.Activated = true;
                    break;
                case AsusACPI.PerformanceBalanced:
                    buttonBalanced.Activated = true;
                    break;
                case Modes.WindowsSlot:
                    break;
                default:
                    buttonCustom.Activated = true;
                    buttonCustom.BorderColor = Modes.GetBase(mode) switch
                    {
                        AsusACPI.PerformanceSilent => colorEco,
                        AsusACPI.PerformanceTurbo => colorTurbo,
                        AsusACPI.PerformanceFullSpeed => Color.Orange,
                        _ => colorStandard,
                    };
                    break;
            }

            foreach (var item in contextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Tag is not null)
                {
                    menuItem.Checked = ((int)menuItem.Tag == mode);
                }
            }
        }


    private void InitSixModes()
    {
        buttonWindows.Name = "buttonWindows";
        buttonCustom.Name = "buttonCustom";
        buttonWindows.Text = Modes.GetName(Modes.WindowsSlot);
        buttonCustom.Text = "커스텀";
        foreach (var b in new[] { buttonWindows, buttonCustom })
        {
            b.Margin = new Padding(4);
            b.BorderRadius = 5;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Image = Properties.Resources.icons8_flash_48;
            b.ImageAlign = ContentAlignment.BottomCenter;
            b.TextImageRelation = TextImageRelation.ImageAboveText;
        }
        buttonWindows.BorderColor = colorStandard;
        buttonCustom.BorderColor = colorCustom;
        buttonWindows.Click += (_, _) => Program.modeControl.SetPerformanceMode(Modes.WindowsSlot);
        buttonCustom.Click += (_, _) =>
        {
            Program.modeControl.SetPerformanceMode(Modes.GetCustomSlot());
            SetContextMenu();
        };
        PerformanceModeLayout.Configure(tablePerf,
            new Control[] { buttonWindows, buttonSilent, buttonBalanced, buttonTurbo, buttonManual, buttonCustom });
        // Fans + Power is an editor/action, never a seventh performance mode.
        buttonFans.Image = null;
        buttonFans.TextImageRelation = TextImageRelation.Overlay;
        buttonFans.Dock = DockStyle.Top;
        buttonFans.Height = 36;
        buttonFans.Margin = new Padding(2);
        panelPerformance.Controls.Add(buttonFans);
        panelPerformance.Controls.SetChildIndex(buttonFans, 0);
        modeHelp.SetToolTip(buttonWindows,
            "GHelper OS 관리: 펌웨어 표준(ACPI 0)으로 초기화하고 저장한 Windows 전원 계획/모드/부스트를 복원합니다. " +
            "팬·전력·GPU 커스텀 자동 적용은 중단합니다. ProArt 제조사 Windows 정책과 동일함을 검증한 모드가 아닙니다.");
        modeHelp.SetToolTip(buttonCustom, "기존 GHelper 사용자 프로필을 선택합니다. 팬 + 전력에서 편집·추가·선택할 수 있습니다. 수동(ACPI 4)과 별개입니다.");
    }
}
