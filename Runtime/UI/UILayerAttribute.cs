using System;

namespace LFramework.UI
{
    public class UILayerAttribute:Attribute
    {
       public UIPanelLayer layer { get;}

        public UILayerAttribute(UIPanelLayer layer)
        {
            this.layer = layer;
        }
    }
}