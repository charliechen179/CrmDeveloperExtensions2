﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SparkleXrm.Tasks
{
    /// <summary>
    /// Responsible for parsing plugin/workflow activity classes and adding deployment attribute metadata
    /// </summary>
    public class CodeParser
    {
        #region Private Fields
        private string _filePath;
        private string _code;
        private Dictionary<string,Match> _pluginClasses;
        private Dictionary<string, Match> _pluginTypes;
        private Dictionary<string, Match> _workflowTypes;
        private Dictionary<string, string> _namespaces = new Dictionary<string, string>();
        #endregion

        #region Private Constants
        private const string _classRegex = @"((public( sealed)? class (?'class'[\w]*)[\W]*?)((?'plugin':[\W]*?((IPlugin)|(PluginBase)|(Plugin)))|(?'wf':[\W]*?CodeActivity)))";
        private const string _attributeRegex = @"([ ]*?)\[CrmPluginRegistration\(([\W\w\s]+?)(\)\])([ ]*?(\r\n|\r|\n))";
        private const string _namespaceRegEx = @"namespace (?'ns'[\w.]*)";
        #endregion

        #region Constructors
        public CodeParser(Uri filePath) 
        {
            _filePath = filePath.OriginalString;
            _code = File.ReadAllText(_filePath);
            Init();
        }
        public CodeParser(string code)
        {
            _code = code;
            Init();
        }

        private void Init()
        {
            var classMatches = Regex.Matches(_code, _classRegex).Cast<Match>().Where(m => m.Groups.Count > 3).ToArray();
            var classes = classMatches.ToDictionary(delegate (Match match)
            {
                return match.Groups["class"].Value;
            });

            var namespaces = Regex.Matches(_code, _namespaceRegEx).Cast<Match>().Reverse().ToDictionary(delegate (Match match)
            {
                return match.Index;
            });

            _pluginClasses = new Dictionary<string, Match>();
            foreach (var match in classes)
            {
                // Find the namespace before the position
                var namespaceMatch = namespaces.Values.FirstOrDefault(n => n.Index <= match.Value.Index);
                if (namespaceMatch == null)
                    throw new Exception(String.Format("Cannot find namespace for class {0}", match.Value));

                _namespaces[match.Key] = namespaceMatch.Groups["ns"].Value;
                _pluginClasses[namespaceMatch.Groups["ns"].Value + "." + match.Key] = classes[match.Key];
            }

            _pluginTypes = classMatches.Where(a => a.Groups["plugin"].Length > 0).ToDictionary(delegate (Match match)
            {
                var className = match.Groups["class"].Value;
                return _namespaces[className] + "." + className;
            });

            _workflowTypes = classMatches.Where(a => a.Groups["wf"].Length > 0).ToDictionary(delegate (Match match)
            {
                var className = match.Groups["class"].Value;
                return _namespaces[className] + "." + className;
            });
        }
        #endregion

        #region Properties
        public string Code
        {
            get { return _code; }
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public List<string> ClassNames
        {
            get { return _pluginClasses.Keys.ToList(); }
        }

        public int PluginCount
        {
            get { return _pluginClasses.Keys.Count; }
        }
        #endregion

        #region Methods
        public bool IsPlugin(string className)
        {
            return _pluginTypes.Keys.Contains(className);
        }

        public bool IsWorkflowActivity(string className)
        {
            return _workflowTypes.Keys.Contains(className);
        }

        public int RemoveExistingAttributes()
        {
            int count = 0;
            MatchEvaluator evaluator = delegate
            {
                count++;
                return "" ;
            };
            
            _code = Regex.Replace(_code, _attributeRegex, evaluator);
            return count;
        }     

        public void AddAttribute(CrmPluginRegistrationAttribute attribute, string className)
        {
            // Locate the start of the class and add the attribute above
            var classLocation = _pluginClasses.ContainsKey(className) ? _pluginClasses[className] : null;
            if (classLocation == null)
                throw new Exception(String.Format("Cannot find class {0}", className));

            var pos = _code.IndexOf(classLocation.Value);

            // Find previouse line break
            var lineBreak = _code.LastIndexOf("\n", pos - 1);

            // Indentation
            var indentation = _code.Substring(lineBreak, pos - lineBreak);

            // Add the attribute
            var attributeCode = attribute.GetAttributeCode(indentation);
            //var attributeCode = attribute.GetAttributeCode("");

            //attributeCode = attributeCode.Replace("\r\n", String.Empty).Replace("\n", String.Empty).Replace("\r", String.Empty);

            //TODO: see about getting everything on 1 line AND not getting inconsistent line ending warnings
            //TODO: see about making sure everything gets inserted into #region if in place

            // Insert   
            _code = _code.Insert(lineBreak, attributeCode);
            //_code = _code.Insert(lineBreak, indentation + attributeCode);
        }
        #endregion
    }
}