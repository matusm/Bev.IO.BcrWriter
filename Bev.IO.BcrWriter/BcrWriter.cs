//*******************************************************************************************
//
// Library for writing GPS data files according to ISO 25178-7, ISO 25178-71 and EUNA 15178. 
//
// Usage:
//   1) instantiate class;
//   2) provide required properties;
//   3) provide topography data (as array or list) by calling PrepareMainSection(double[]);
//   4) optionally produce a file trailer by calling PrepareTrailerSection(Dictonary<string, string>);
//   5) finally produce the output file by calling WriteToFile(string).
//
// Caveat:
//   PrepareMainSection(double[]) multiplies the z-data with 1e6 (assuming data is in m)
//   PrepareMainSection(int[]) uses z-data unmodified (assuming data is in µm)
//
// Known problems and restrictions:
//   most properties must be set in advance, otherwise no output will be generated
//   NumberOfPointsPerProfile and NumberOfProfiles must not be modified during operation
//   only double data is supported
//
// Author: Michael Matus, 2017
//   1.1.0	support for int data, 2020
//   1.0.1	refactored source code, 2020
//   1.0.0	first working version, 2017
//
//*******************************************************************************************


using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Bev.IO.BcrWriter
{
    public class BcrWriter
    {

        #region Private Fields

        ZDataType zDataType;

        /// <summary>
        /// The whole file contents (three sections) is stored in three StringBuilders.
        /// </summary>
        private StringBuilder headerSectionSb;
        private StringBuilder dataSectionSb;
        private StringBuilder trailerSectionSb;

        #endregion

        #region Properties

        /// <summary>
        /// If true use ISO 25178-71 format, if false use the legacy BCR format.
        /// </summary>
        public bool IsIsoFormat { get; set; }

        /// <summary>
        /// If true forces the ".SDF" extension for the output file name.
        /// </summary>
        public bool ForceDefaultFileExtension { get; set; }

        /// <summary>
        /// The number of points per profile. Mandatory for file header.
        /// </summary>
        public int NumberOfPointsPerProfile { get; set; }

        /// <summary>
        /// The number of profiles. Mandatory for file header.
        /// </summary>
        public int NumberOfProfiles { get; set; }

        /// <summary>
        /// The point spacing in m. Mandatory for file header.
        /// </summary>
        public double XScale { get; set; }

        /// <summary>
        /// The profile spacing in m. Mandatory for file header.
        /// </summary>
        public double YScale { get; set; }

        /// <summary>
        /// The scan's creation date. Mandatory for file header.
        /// </summary>
        public DateTime CreationDate { get; set; }

        /// <summary>
        /// The scan's modification date. Mandatory for file header.
        /// </summary>
        public DateTime ModificationDate { get; set; }

        /// <summary>
        /// The instrument identifier. Mandatory for file header.
        /// </summary>
        public string ManufacurerId { get; set; }
        #endregion

        #region Ctor
        /// <summary>
        /// Creates an instance of the BcrWriter class.
        /// Sets some properties to default values.
        /// </summary>
        public BcrWriter()
        {
            // I am not completely confident on this, but it works.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            ForceDefaultFileExtension = true;
            IsIsoFormat = false;
            zDataType = ZDataType.None;
            ModificationDate = DateTime.UtcNow;
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Writes the data (if present) to a text file.
        /// </summary>
        /// <param name="outFileName">The filename.</param>
        public void WriteToFile(string outFileName)
        {
            // check if data present
            if (string.IsNullOrWhiteSpace(GetFileContent()))
                return;
            // change file name extension
            string fileName = outFileName;
            if(ForceDefaultFileExtension)
                fileName = Path.ChangeExtension(outFileName, ".sdf");
            // write the file
            StreamWriter hOutFile = File.CreateText(fileName);
            hOutFile.Write(GetFileContent());
            hOutFile.Close();
        }

        /// <summary>
        /// Returns the formated data (if present) as string.
        /// </summary>
        /// <returns>The formated data.</returns>
        public string GetFileContent()
        {
            // check if data is already prepared
            if (headerSectionSb == null)
                return "";
            if (dataSectionSb == null)
                return "";
            string returnString = headerSectionSb.ToString() + dataSectionSb.ToString();
            if (trailerSectionSb != null)
                returnString += trailerSectionSb.ToString();
            return returnString;
        }

        /// <summary>
        /// Prepares the header and data section. 
        /// "Record 1" and "Record 2" according to ISO 25178-71
        /// </summary>
        /// <param name="topographyData">The topography data in units of meter.</param>
        public void PrepareMainSection(double[] topographyData)
        {
            zDataType = ZDataType.Double;
            // perform some validity checks
            if (topographyData == null)
                return;
            if (topographyData.Length != NumberOfPointsPerProfile * NumberOfProfiles)
                return;
            // prepare the header section
            PrepareHeaderSection();
            // create the StringBuilder for the data section
            dataSectionSb = new StringBuilder();
            // write the scan data in µm
            foreach (double z in topographyData)
            {
                if (double.IsNaN(z))
                {
                    dataSectionSb.AppendLine("BAD"); // ISO 25178-71 clause 5.3.2
                }
                else
                {
                    dataSectionSb.AppendLine($"{z*1e6:F5}");
                }
            }
            // End of section delimiter
            dataSectionSb.AppendLine("*");
        }

        /// <summary>
        /// Prepares the header and data section. 
        /// "Record 1" and "Record 2" according to ISO 25178-71
        /// </summary>
        /// <param name="topographyData">The topography data in units of micrometer.</param>
        public void PrepareMainSection(int[] topographyData)
        {
            zDataType = ZDataType.Int32;
            // perform some validity checks
            if (topographyData == null)
                return;
            if (topographyData.Length != NumberOfPointsPerProfile * NumberOfProfiles)
                return;
            // prepare the header section
            PrepareHeaderSection();
            // create the StringBuilder for the data section
            dataSectionSb = new StringBuilder();
            // write the scan data in µm
            foreach (double z in topographyData)
            {
                dataSectionSb.AppendLine($"{z}");
            }
            // End of section delimiter
            dataSectionSb.AppendLine("*");
        }

        /// <summary>
        /// Prepares the (optional) trailer section.
        /// "Record 3" according to ISO 25178-71
        /// </summary>
        /// <param name="trailerEntries"></param>
        public void PrepareTrailerSection(Dictionary<string, string> trailerEntries)
        {
            // perform some validity checks
            if (trailerEntries == null)
                return;
            // create the StringBuilder for the file trailer section
            trailerSectionSb = new StringBuilder();
            // add assembly version information as the last entry
            trailerEntries["AssemblyName"] = Assembly.GetEntryAssembly().GetName().Name;
            Version ver = Assembly.GetEntryAssembly().GetName().Version;
            trailerEntries["AssemblyVersion"] = string.Format("{0}.{1}", ver.Major, ver.Minor);
            // padd all keys to the same length
            Dictionary<string, string> niceEntries = BeautifyKeyStrings(trailerEntries);
            // iterate the dictonary
            foreach (string k in niceEntries.Keys)
                trailerSectionSb.AppendLine(BcrTrailerLine(k, niceEntries[k]));
            // End of section delimiter
            trailerSectionSb.AppendLine("*");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Prepares the SDF file header section. Respective properties must be set in advance.
        /// Constitutes "Record 1" according to ISO 25178-71
        /// </summary>
        private void PrepareHeaderSection()
        {
            // for single profiles YScale must be 0
            if (NumberOfProfiles == 1)
                YScale = 0;
            // the SDF definition suffers from the historical restriction of 65535 points per profile 
            // there is no error message up to now, it is just not possible to create data.
            if (NumberOfPointsPerProfile > ushort.MaxValue)
                return;
            if (NumberOfProfiles > ushort.MaxValue)
                return;
            // instantiate the StringBuilder for the header section
            headerSectionSb = new StringBuilder();
            if (IsIsoFormat)
            {
                headerSectionSb.AppendLine("aISO-1.0");
            }
            else
            {
                headerSectionSb.AppendLine("aBCR-1.0");
            }
            headerSectionSb.AppendLine($"ManufacID   = {ManufacurerId}");
            headerSectionSb.AppendLine($"CreateDate  = {CreationDate.ToString("ddMMyyyyHHmm")}");
            headerSectionSb.AppendLine($"ModDate     = {ModificationDate.ToString("ddMMyyyyHHmm")}");
            headerSectionSb.AppendLine($"NumPoints   = {NumberOfPointsPerProfile}");
            headerSectionSb.AppendLine($"NumProfiles = {NumberOfProfiles}");
            headerSectionSb.AppendLine($"Xscale      = {XScale.ToString("G5")}");
            headerSectionSb.AppendLine($"Yscale      = {YScale.ToString("G5")}");
            headerSectionSb.AppendLine("Zscale      = 1E-06"); // always use µm
            headerSectionSb.AppendLine("Zresolution = -1"); // clause 5.2.8, do not modify!
            headerSectionSb.AppendLine("Compression = 0"); // clause 5.2.9, do not modify!
            switch (zDataType)
            {
                case ZDataType.Double:
                    headerSectionSb.AppendLine("DataType    = 7"); 
                    break;
                case ZDataType.Int32:
                    headerSectionSb.AppendLine("DataType    = 6"); 
                    break;
                case ZDataType.Int16:
                    headerSectionSb.AppendLine("DataType    = 5"); 
                    break;
                default:
                    headerSectionSb.AppendLine("DataType    = ?"); // this should not happen!
                    break;
            }
            headerSectionSb.AppendLine("CheckType   = 0"); // clause 5.2.11, do not modify!
            headerSectionSb.AppendLine("*");
        }

        /// <summary>
        /// Formats a given directory by trimming the values (strings) and padding the keys to the maximum length of all keys (strings).
        /// </summary>
        /// <param name="rawDictonary">The dictonary to be beautified.</param>
        /// <returns>The beautified dictonary.</returns>
        private Dictionary<string, string> BeautifyKeyStrings(Dictionary<string, string> rawDictonary)
        {
            // determine the length of the longest (trimmed) key
            int maxKeyLength = 0;
            foreach (string k in rawDictonary.Keys)
                if (k.Trim().Length > maxKeyLength) maxKeyLength = k.Trim().Length;
            // build the beautified dictonary
            Dictionary<string, string> processedDictonary = new Dictionary<string, string>();
            foreach (string k in rawDictonary.Keys)
                processedDictonary[k.Trim().PadRight(maxKeyLength)] = rawDictonary[k].Trim();
            return processedDictonary;
        }

        /// <summary>
        /// Prepares a line for the file trailer in on of two possible formats.
        /// </summary>
        /// <param name="key">The metadata key.</param>
        /// <param name="value">The metadata value for the key.</param>
        /// <returns>A line of text for the file trailer section.</returns>
        private string BcrTrailerLine(string key, string value)
        {
            return BcrTrailerLine(key, value, "");
        }

        /// <summary>
        /// Prepares a line for the file trailer in on of two possible formats.
        /// A comment can be included also.
        /// </summary>
        /// <param name="key">The metadata key.</param>
        /// <param name="value">The metadata value for the key.</param>
        /// <param name="comment">A comment.</param>
        /// <returns>A line of text for the file trailer section.</returns>
        /// <remarks>
        /// Any information following a ";" is considered as a comment according to section 8.3 of ISO 25178-7.
        /// This behaviour is not defined in ISO 25178-71 however, so comments are suppressed in this case.
        /// </remarks>
        private string BcrTrailerLine(string key, string value, string comment)
        {
            string returnString;
            if(IsIsoFormat)
                returnString = $"<{key.Trim()}> {value.Trim()} </{key.Trim()}>";
            else
                returnString = $"{key.TrimStart()} = {value.Trim()}";
            // append a comment if appropiate
            if (!string.IsNullOrWhiteSpace(comment) && !IsIsoFormat)
                returnString += $" ; {comment.Trim()}";
            return returnString;
        }

        #endregion
    }

    /// <summary>
    /// Allowed data types, ISO 25178-71 clause 5.2.10
    /// </summary>
    enum ZDataType
    {
        None,
        Int16, // short, not implemented
        Int32, // int
        Double // double
    }
}
