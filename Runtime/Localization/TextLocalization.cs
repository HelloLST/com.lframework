using UnityEngine.UI;

namespace LFramework
{
    public class TextLocalization : UILocalization<Text>
    {
        protected override void RefreshShow()
        {
            m_Target.text = GetLocalizationData();
        }
    }
}
