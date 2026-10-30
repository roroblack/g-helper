using Microsoft.Win32;

namespace GHelper.Fan;

// Queue-only UI consumer. Separate from CPU/tray/overlay/min-max/fan inputs.
internal sealed class ThermalZonePanel : UserControl
{
    private readonly ThermalZoneReader reader;
    private readonly TableLayoutPanel rows=new() { AutoSize=true, Dock=DockStyle.Top, ColumnCount=3 };
    private readonly System.Windows.Forms.Timer timer=new() { Interval=250 };
    private readonly ToolTip tips=new() { AutoPopDelay=30000 };
    private string signature="";
    internal ThermalZonePanel(ThermalZoneReader reader)
    {
        this.reader=reader;
        AutoSize=true;Dock=DockStyle.Top;
        var group=new GroupBox { Text="Thermal zones (ACPI/WMI)",AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(10,22,10,10) };
        var stack=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Top,ColumnCount=1};
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        stack.Controls.Add(rows);
        stack.Controls.Add(new Label {AutoSize=true,MaximumSize=new Size(800,0),Text="Physical identity unknown · upstream/firmware freshness unknown. WMI query freshness ≠ physical freshness; firmware cached/fallback possible (THRM LTMP/default 60°C). Not verified as CPU die or board temperature; not for safety/control.",Margin=new Padding(4,10,4,4)});
        group.Controls.Add(stack);Controls.Add(group);
        timer.Tick+=(_,_)=>RefreshRows();
        RefreshRows(false);
    }
    internal void Start() { if(!IsDisposed) { RefreshRows();timer.Start(); } }
    internal void RefreshRows(bool request=true)
    {
        var samples=reader.Peek(); // expire BEFORE queuing a potentially blocked query
        string keys=string.Join("\n",samples.Select(x=>x.Name));
        if(keys!=signature || rows.Controls.Count==0)
        {
            signature=keys;
            foreach(Control c in rows.Controls.Cast<Control>().ToArray()) c.Dispose();
            rows.Controls.Clear();rows.RowStyles.Clear();rows.RowCount=samples.Length;
            for(int i=0;i<samples.Length;i++)
            {
                string name=samples[i].Name;
                string label=name switch { @"\_TZ.THRM"=>"THRM (thermal zone)", @"\_TZ.THZ0"=>"THZ0 (thermal zone)", _=>name+" (thermal zone)" };
                rows.Controls.Add(new Label {Text=label,AutoSize=true,Margin=new Padding(4,6,4,6)},0,i);
                rows.Controls.Add(new Label {Name="zone-value:"+name,AutoSize=true,Margin=new Padding(4,6,4,6)},1,i);
                rows.Controls.Add(new Label {Name="zone-status:"+name,AutoSize=true,MaximumSize=new Size(490,0),Margin=new Padding(4,6,4,6)},2,i);
            }
        }
        for(int i=0;i<samples.Length;i++)
        {
            var s=samples[i];var value=rows.GetControlFromPosition(1,i)!;var status=rows.GetControlFromPosition(2,i)!;
            value.Text=s.Display;
            status.Text=$"{s.Status} · acquired UTC {(s.AcquiredAtUtc==DateTimeOffset.MinValue ? "N/A" : s.AcquiredAtUtc.ToString("HH:mm:ss"))} · firmware freshness unknown";
            for(int col=0;col<3;col++)tips.SetToolTip(rows.GetControlFromPosition(col,i)!,s.Details);
        }
        if(request)reader.Request();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) {timer.Stop();timer.Dispose();tips.Dispose();}
        base.Dispose(disposing);
    }
}
