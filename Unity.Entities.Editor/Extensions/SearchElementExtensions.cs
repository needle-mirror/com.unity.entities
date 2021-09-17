using System;
using Unity.Properties.UI;

namespace Unity.Entities.Editor
{
    static class SearchElementExtensions
    {
        public static void AddSearchFilterCallbackWithPopupItem<TData, TFilter>(this SearchElement searchElement, string token, Func<TData, TFilter> getSearchDataFunc, string filterText, string filterTooltip = "", string[] supportedOperatorTypes = null)
        {
            searchElement.AddSearchFilterCallback(token, getSearchDataFunc, supportedOperatorTypes);
            searchElement.AddSearchFilterPopupItem(token, filterText, filterTooltip);
        }
    }
}
