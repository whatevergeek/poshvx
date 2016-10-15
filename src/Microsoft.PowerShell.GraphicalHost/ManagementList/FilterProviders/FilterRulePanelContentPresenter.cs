﻿//-----------------------------------------------------------------------
// <copyright file="FilterRulePanelContentPresenter.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;

    /// <summary>
    /// The FilterRulePanelContentPresenter selects a template based upon the ContentConverter
    /// provided.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public class FilterRulePanelContentPresenter : ContentPresenter
    {
        /// <summary>
        /// Initializes a new instance of the FilterRulePanelContentPresenter class.
        /// </summary>
        public FilterRulePanelContentPresenter()
        {
            Binding b = new Binding("FilterRuleTemplateSelector");
            b.RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(FilterRulePanel), 1);
            this.SetBinding(ContentTemplateSelectorProperty, b);
        }

        /// <summary>
        /// Gets or sets an IValueConverter used to convert the Content
        /// value.
        /// </summary>
        public IValueConverter ContentConverter
        {
            get;
            set;
        }

        /// <summary>
        /// Chooses a template based upon the provided ContentConverter.
        /// </summary>
        /// <returns>
        /// Returns a DataTemplate.
        /// </returns>
        protected override DataTemplate ChooseTemplate()
        {
            if (null == this.ContentTemplateSelector || null == this.ContentConverter)
            {
                return base.ChooseTemplate();
            }

            object converterContent = this.ContentConverter.Convert(this.Content, typeof(object), null, System.Globalization.CultureInfo.CurrentCulture);
            return this.ContentTemplateSelector.SelectTemplate(converterContent, this);
        }
    }
}
