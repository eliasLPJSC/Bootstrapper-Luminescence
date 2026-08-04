using System.Drawing;
using System.Windows.Forms;

namespace Luminescence_v1._03E
{
    public class NoScrollFlowLayoutPanel : FlowLayoutPanel
    {
        protected override Point ScrollToControl(Control activeControl)
        {
            if (this.DesignMode || System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            {
                return base.ScrollToControl(activeControl);
            }

            return DisplayRectangle.Location;
        }
    }
}