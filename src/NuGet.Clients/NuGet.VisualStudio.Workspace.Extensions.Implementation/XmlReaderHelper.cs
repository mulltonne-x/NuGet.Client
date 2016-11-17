// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Xml;

namespace NuGet.VisualStudio.Workspace.Extensions.Implementation
{
    /// <summary>
    /// Xml Reader Helper class
    /// </summary>
    internal static class XmlReaderHelper
    {
        /// <summary>
        /// Helper method to create the <see cref="XmlReader"/>
        /// </summary>
        /// <param name="filePath">The Xml file path.</param>
        /// <returns>The Xml reader.</returns>
        internal static XmlReader CreateXmlReader(string filePath)
        {
            XmlReaderSettings xmlReaderSettings = new XmlReaderSettings();
            xmlReaderSettings.XmlResolver = null;

            return XmlReader.Create(filePath, xmlReaderSettings);
        }
    }
}
