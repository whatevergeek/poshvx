/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Management.Automation;
using System.Net;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Response object for html content without DOM parsing
    /// </summary>
    public partial class BasicHtmlWebResponseObject : WebResponseObject
    {
        #region Properties

        /// <summary>
        /// gets or protected sets the Content property
        /// </summary>
        public new string Content { get; private set; }

        private WebCmdletElementCollection _inputFields;

        /// <summary>
        /// gets the Fields property
        /// </summary>
        public WebCmdletElementCollection InputFields
        {
            get
            {
                if (_inputFields == null)
                {
                    EnsureHtmlParser();

                    List<PSObject> parsedFields = new List<PSObject>();
                    MatchCollection fieldMatch = s_inputFieldRegex.Matches(Content);
                    foreach (Match field in fieldMatch)
                    {
                        parsedFields.Add(CreateHtmlObject(field.Value, "INPUT"));
                    }

                    _inputFields = new WebCmdletElementCollection(parsedFields);
                }

                return _inputFields;
            }
        }

        private WebCmdletElementCollection _links;

        /// <summary>
        /// gets the Links property
        /// </summary>
        public WebCmdletElementCollection Links
        {
            get
            {
                if (_links == null)
                {
                    EnsureHtmlParser();

                    List<PSObject> parsedLinks = new List<PSObject>();
                    MatchCollection linkMatch = s_linkRegex.Matches(Content);
                    foreach (Match link in linkMatch)
                    {
                        parsedLinks.Add(CreateHtmlObject(link.Value, "A"));
                    }

                    _links = new WebCmdletElementCollection(parsedLinks);
                }

                return _links;
            }
        }

        private WebCmdletElementCollection _images;

        /// <summary>
        /// gets the Images property
        /// </summary>
        public WebCmdletElementCollection Images
        {
            get
            {
                if (_images == null)
                {
                    EnsureHtmlParser();

                    List<PSObject> parsedImages = new List<PSObject>();
                    MatchCollection imageMatch = s_imageRegex.Matches(Content);
                    foreach (Match image in imageMatch)
                    {
                        parsedImages.Add(CreateHtmlObject(image.Value, "IMG"));
                    }

                    _images = new WebCmdletElementCollection(parsedImages);
                }

                return _images;
            }
        }

        #endregion Properties

        #region Private Fields

        private static Regex s_tagRegex;
        private static Regex s_attribsRegex;
        private static Regex s_attribNameValueRegex;
        private static Regex s_inputFieldRegex;
        private static Regex s_linkRegex;
        private static Regex s_imageRegex;

        #endregion Private Fields

        #region Methods

        private void EnsureHtmlParser()
        {
            if (s_tagRegex == null)
            {
                s_tagRegex = new Regex(@"<\w+((\s+[^""'>/=\s\p{Cc}]+(\s*=\s*(?:"".*?""|'.*?'|[^'"">\s]+))?)+\s*|\s*)/?>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            if (s_attribsRegex == null)
            {
                s_attribsRegex = new Regex(@"(?<=\s+)([^""'>/=\s\p{Cc}]+(\s*=\s*(?:"".*?""|'.*?'|[^'"">\s]+))?)",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            if (s_attribNameValueRegex == null)
            {
                s_attribNameValueRegex = new Regex(@"([^""'>/=\s\p{Cc}]+)(?:\s*=\s*(?:""(.*?)""|'(.*?)'|([^'"">\s]+)))?",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            if (s_inputFieldRegex == null)
            {
                s_inputFieldRegex = new Regex(@"<input\s+[^>]*(/>|>.*?</input>)",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            if (s_linkRegex == null)
            {
                s_linkRegex = new Regex(@"<a\s+[^>]*(/>|>.*?</a>)",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            if (s_imageRegex == null)
            {
                s_imageRegex = new Regex(@"<img\s+[^>]*>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
        }

        private PSObject CreateHtmlObject(string html, string tagName)
        {
            PSObject elementObject = new PSObject();

            elementObject.Properties.Add(new PSNoteProperty("outerHTML", html));
            elementObject.Properties.Add(new PSNoteProperty("tagName", tagName));

            ParseAttributes(html, elementObject);

            return elementObject;
        }

        private void ParseAttributes(string outerHtml, PSObject elementObject)
        {
            // We might get an empty input for a directive from the HTML file
            if (!string.IsNullOrEmpty(outerHtml))
            {
                // Extract just the opening tag of the HTML element (omitting the closing tag and any contents, 
                // including contained HTML elements)
                var match = s_tagRegex.Match(outerHtml);

                // Extract all the attribute specifications within the HTML element opening tag
                var attribMatches = s_attribsRegex.Matches(match.Value);

                foreach (Match attribMatch in attribMatches)
                {
                    // Extract the name and value for this attribute (allowing for variations like single/double/no
                    // quotes, and no value at all)
                    var nvMatches = s_attribNameValueRegex.Match(attribMatch.Value);
                    Debug.Assert(nvMatches.Groups.Count == 5);

                    // Name is always captured by group #1
                    string name = nvMatches.Groups[1].Value;

                    // The value (if any) is captured by group #2, #3, or #4, depending on quoting or lack thereof
                    string value = null;
                    if (nvMatches.Groups[2].Success)
                    {
                        value = nvMatches.Groups[2].Value;
                    }
                    else if (nvMatches.Groups[3].Success)
                    {
                        value = nvMatches.Groups[3].Value;
                    }
                    else if (nvMatches.Groups[4].Success)
                    {
                        value = nvMatches.Groups[4].Value;
                    }

                    elementObject.Properties.Add(new PSNoteProperty(name, value));
                }
            }
        }

        /// <summary>
        /// Reads the response content from the web response.
        /// </summary>
        private void InitializeContent()
        {
            string contentType = ContentHelper.GetContentType(BaseResponse);
            if (ContentHelper.IsText(contentType))
            {
                // fill the Content buffer
                string characterSet = WebResponseHelper.GetCharacterSet(BaseResponse);
                this.Content = StreamHelper.DecodeStream(RawContentStream, characterSet);
            }
            else
            {
                this.Content = string.Empty;
            }
        }

        #endregion Methods
    }
}
