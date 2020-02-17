using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    internal class PostprocessedILWindow : EditorWindow
    {
        private enum DisplayLanguage
        {
            CSharp,
            IL
        }

        private static readonly Dictionary<TypeDefinition, string[]> _generatedTypesAndDecompiledCSharpCode = new Dictionary<TypeDefinition, string[]>();
        private static readonly Dictionary<TypeDefinition, string[]> _generatedTypesAndDecompiledILCode = new Dictionary<TypeDefinition, string[]>();

        private static TypeDefinition[] _allDOTSCompilerGeneratedTypes;
        private static TypeDefinition[] _dotsCompilerGeneratedTypesToDisplay;

        private DisplayLanguage _currentDisplayLanguage = DisplayLanguage.CSharp;
        private TypeDefinition _currentlySelectedTypeDefinition;
        private string[] _currentlyDisplayedDecompiledCode;
        
        private Process _decompilationProcess;
        private ListView _decompiledCodeField;
        private Label _decompilationStatusLabel;
        
        private bool _userMadeAtLeastOneSelection;
        
        private DecompilationStatus _currentDecompilationStatus;
        private int _decompilationDurationSoFar;
        
        private const int EstimatedNumFramesNeededForDecompilationToFinish = 100;

        private enum DecompilationStatus
        {
            InProgress,
            Complete
        }

        [MenuItem("DOTS/DOTS Compiler/Open Inspector...")]
        private static void PostprocessedILInspector()
        {
            GetWindow<PostprocessedILWindow>().Show();
        }

        private void OnGUI()
        {
            if (!_userMadeAtLeastOneSelection)
            {
                return;
            }
            
            switch (_currentDecompilationStatus)
            {
                case DecompilationStatus.Complete:
                {
                    _decompilationStatusLabel.text =
                        $"Currently displaying decompiled {(_currentDisplayLanguage == DisplayLanguage.CSharp ? "C#" : "IL")} code.";
                    Repaint();
                
                    return;
                }
                case DecompilationStatus.InProgress when _decompilationDurationSoFar < EstimatedNumFramesNeededForDecompilationToFinish:
                {
                    _decompilationStatusLabel.text = "Decompilation in progress. Please be patient...";
                    _decompilationDurationSoFar++;
                
                    Repaint();
                
                    return;
                }
                default:
                {
                    switch (_currentDisplayLanguage)
                    {
                        case DisplayLanguage.CSharp:
                        {
                            _generatedTypesAndDecompiledCSharpCode.Add(
                                _currentlySelectedTypeDefinition,
                                _decompilationProcess.StandardOutput.ReadToEnd().Split(new[] {Environment.NewLine}, StringSplitOptions.None));
                    
                            DisplayDecompiledCode(DisplayLanguage.CSharp);
                            break;
                        }

                        case DisplayLanguage.IL:
                        {
                            _generatedTypesAndDecompiledILCode.Add(
                                _currentlySelectedTypeDefinition,
                                _decompilationProcess.StandardOutput.ReadToEnd().Split(new[] {Environment.NewLine}, StringSplitOptions.None));
                    
                            DisplayDecompiledCode(DisplayLanguage.IL);
                            break;
                        }
                    }

                    _decompilationDurationSoFar = 0;
                    break;
                }
            }
        }

        private void OnEnable()
        {
            this.minSize = new Vector2(1500f, 400f);

            if (_allDOTSCompilerGeneratedTypes == null)
            {
                _allDOTSCompilerGeneratedTypes =
                    _dotsCompilerGeneratedTypesToDisplay =
                        TypeCache.GetTypesWithAttribute<DOTSCompilerGeneratedAttribute>()
                                 .Select(GetTypeDefinition)
                                 .Where(t => t != null)
                                 .OrderBy(t => t.GetUserFriendlyName())
                                 .ToArray();
            }

            VisualElement searchBarAndGeneratedTypeListView = new VisualElement
            {
                style = {width = new StyleLength(new Length(20, LengthUnit.Percent))},
                name = "Search bar and list view for DOTS compiler-generated types"
            };

            ListView generatedTypesListView =
                new ListView(
                    itemsSource: _dotsCompilerGeneratedTypesToDisplay,
                    itemHeight: 15,
                    makeItem: () => new Label {style = {color = new Color(0.71f, 1f, 0f)}},
                    bindItem: (element, index) =>
                    {
                        ((Label) element).text = _dotsCompilerGeneratedTypesToDisplay[index].GetUserFriendlyName();
                    })
                {
                    style =
                    {
                        width = new StyleLength(new Length(100, LengthUnit.Percent)),
                        height = new StyleLength(new Length(98, LengthUnit.Percent)),
                        top = new StyleLength(new Length(10, LengthUnit.Pixel))
                    },
                    name = "List view for DOTS compiler-generated types"
                };
            var searchFieldToolbar = new Toolbar
            {
                style =
                {
                    width = new StyleLength(new Length(100, LengthUnit.Percent)),
                    flexGrow = 0
                }
            };
            var searchField = new ToolbarSearchField
            {
                style = {width = new StyleLength(new Length(98, LengthUnit.Percent))},
                name = "Search bar for DOTS compiler-generated types"
            };
            searchField.RegisterValueChangedCallback(changeEvent =>
            {
                _dotsCompilerGeneratedTypesToDisplay =
                    string.IsNullOrWhiteSpace(changeEvent.newValue)
                        ? _allDOTSCompilerGeneratedTypes
                        : _allDOTSCompilerGeneratedTypes
                            .Where(t => IsFilteredType(t, changeEvent.newValue))
                            .ToArray();
                
                generatedTypesListView.itemsSource = _dotsCompilerGeneratedTypesToDisplay;
            });
            searchFieldToolbar.Add(searchField);
            
            searchBarAndGeneratedTypeListView.style.flexDirection = FlexDirection.Column;
            searchBarAndGeneratedTypeListView.Add(searchFieldToolbar);
            searchBarAndGeneratedTypeListView.Add(generatedTypesListView);

            var canvasForOtherContents = new VisualElement
            {
                style = {width = new StyleLength(new Length(80, LengthUnit.Percent))},
                name = "Canvas for: Font selection tool bar; copy buttons; displaying decompiled code"
            };

            var canvasForDisplayingDecompiledCode = new VisualElement
            {
                style =
                {
                    width = new StyleLength(new Length(100, LengthUnit.Percent)),
                    height = new StyleLength(new Length(95, LengthUnit.Percent))
                },
                name = "Canvas for displaying decompiled code"
            };

            _decompiledCodeField = new ListView(
                itemsSource: _currentlyDisplayedDecompiledCode,
                itemHeight: 15,
                makeItem: () => new Label {style = {color = Color.white}},
                bindItem: (element, i) => ((Label) element).text = $"{i}\t{_currentlyDisplayedDecompiledCode[i]}"
            )
            {
                style =
                {
                    width = new StyleLength(new Length(100, LengthUnit.Percent)),
                    height = new StyleLength(new Length(100, LengthUnit.Percent)),
                    borderLeftWidth = new StyleFloat(5f),
                    borderLeftColor = new StyleColor(Color.grey)
                },
                name = "List view for decompiled code. (A regular text field cannot display that much text.)"
            };
          
            _decompilationStatusLabel = new Label
            {
                style =
                {
                    height = new StyleLength(new Length(100, LengthUnit.Percent)),
                    width = new StyleLength(new Length(600, LengthUnit.Pixel)),
                    unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleRight),
                    color = new Color(0.71f, 1f, 0f)
                }
            };

            #if UNITY_2020
            generatedTypesListView.onSelectionChange += o =>
            {
                _currentlySelectedTypeDefinition = (TypeDefinition)o.Single();
                _userMadeAtLeastOneSelection = true;
                StartDecompilationOrDisplayDecompiledCode();
            };
            #else
            generatedTypesListView.onSelectionChanged += o =>
            {
                _currentlySelectedTypeDefinition = (TypeDefinition)o.Single();
                _userMadeAtLeastOneSelection = true;
                StartDecompilationOrDisplayDecompiledCode();
            };
            #endif
            
            canvasForDisplayingDecompiledCode.style.flexDirection = FlexDirection.Row;
            canvasForDisplayingDecompiledCode.Add(_decompiledCodeField);
            
            var toolBar = new Toolbar
            {
                style =
                {
                    flexGrow = 0,
                    height = new StyleLength(new Length(20, LengthUnit.Pixel)),
                    width = new StyleLength(new Length(100, LengthUnit.Percent))
                },
                name = "Canvas for: font size selector; copy button"
            };

            var fontSizeSelector = new ToolbarMenu
            {
                style =
                {
                    height = new StyleLength(new Length(100, LengthUnit.Percent)),
                    width = new StyleLength(new Length(80, LengthUnit.Pixel))
                },
                text = "Font size",
                name = "Font size selection toolbar"
            };

            foreach (int i in Enumerable.Range(start: 12, count: 7))
            {
                fontSizeSelector.menu.AppendAction(actionName: $"{i}", action =>
                {
                    _decompiledCodeField.style.fontSize = i;
                    _decompiledCodeField.itemHeight = Mathf.CeilToInt(i * 1.5f);
                });
            }
            
            var decompiledLanguageSelector = new ToolbarMenu
            {
                style =
                {
                    height = new StyleLength(new Length(100, LengthUnit.Percent)),
                    width = new StyleLength(new Length(80, LengthUnit.Pixel))
                },
                text = "Language",
                name = "Decompiled language selection toolbar"
            };
            decompiledLanguageSelector.menu.AppendAction(
                "C#", 
                action =>
                {
                    _currentDisplayLanguage = DisplayLanguage.CSharp;
                    StartDecompilationOrDisplayDecompiledCode();
                });
            decompiledLanguageSelector.menu.AppendAction(
                "IL", 
                action =>
                {
                    _currentDisplayLanguage = DisplayLanguage.IL;
                    StartDecompilationOrDisplayDecompiledCode();
                });
            
            var copyDecompiledCodeButton = new ToolbarButton
            {
                style =
                {
                    height = new StyleLength(new Length(100, LengthUnit.Percent)),
                    width = new StyleLength(new Length(150, LengthUnit.Pixel)),
                    unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleCenter)
                },
                text = "Copy decompiled code",
                name = "Button to copy decompiled code"
            };
            copyDecompiledCodeButton.clicked += () =>
                EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, (string[])_decompiledCodeField.itemsSource);
            
            toolBar.style.flexDirection = FlexDirection.RowReverse;
            toolBar.Add(fontSizeSelector);
            toolBar.Add(decompiledLanguageSelector);
            toolBar.Add(copyDecompiledCodeButton);
            toolBar.Add(_decompilationStatusLabel);
            
            canvasForOtherContents.style.flexDirection = FlexDirection.Column;
            canvasForOtherContents.Add(toolBar);
            canvasForOtherContents.Add(canvasForDisplayingDecompiledCode);
            
            rootVisualElement.style.flexDirection = FlexDirection.Row;
            rootVisualElement.Add(searchBarAndGeneratedTypeListView);
            rootVisualElement.Add(canvasForOtherContents);
            
            bool IsFilteredType(TypeDefinition typeDefinition, string userSpecifiedTypeName)
            {
                return CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                           typeDefinition.GetUserFriendlyName(),
                           userSpecifiedTypeName, 
                           CompareOptions.IgnoreCase) >= 0;
            }
        }

        private void StartDecompilationOrDisplayDecompiledCode()
        {
            switch (_currentDisplayLanguage)
            {
                case DisplayLanguage.CSharp:
                {
                    if (_generatedTypesAndDecompiledCSharpCode.ContainsKey(_currentlySelectedTypeDefinition))
                    {
                        DisplayDecompiledCode(DisplayLanguage.CSharp);
                        break;
                    }
                    _decompilationProcess = 
                        Decompiler.StartDecompilationProcesses(_currentlySelectedTypeDefinition, DecompiledLanguage.CSharpOnly).DecompileIntoCSharpProcess;
                    _currentDecompilationStatus = DecompilationStatus.InProgress;

                    break;
                }
                case DisplayLanguage.IL:
                {
                    if (_generatedTypesAndDecompiledILCode.ContainsKey(_currentlySelectedTypeDefinition))
                    {
                        DisplayDecompiledCode(DisplayLanguage.IL);
                        break;
                    }
                    _decompilationProcess = 
                        Decompiler.StartDecompilationProcesses(_currentlySelectedTypeDefinition, DecompiledLanguage.ILOnly).DecompileIntoILProcess;
                    _currentDecompilationStatus = DecompilationStatus.InProgress;

                    break;
                }
            }
        }


        private void DisplayDecompiledCode(DisplayLanguage displayLanguage)
        {
            _currentlyDisplayedDecompiledCode =
                displayLanguage == DisplayLanguage.CSharp
                    ? _generatedTypesAndDecompiledCSharpCode[_currentlySelectedTypeDefinition]
                    : _generatedTypesAndDecompiledILCode[_currentlySelectedTypeDefinition];
            
            _decompiledCodeField.itemsSource = _currentlyDisplayedDecompiledCode;
            _currentDecompilationStatus = DecompilationStatus.Complete;
        }

        private static TypeDefinition GetTypeDefinition(Type type)
        {
            return CreateAssemblyDefinitionFor(type)
                       .MainModule
                       .GetType(type.FullName?.Replace("+", "/"));
        }

        private static AssemblyDefinition CreateAssemblyDefinitionFor(Type type)
        {
            var assemblyLocation = type.Assembly.Location;

            var assemblyDefinition =
                AssemblyDefinition.ReadAssembly(
                    new MemoryStream(
                        buffer: File.ReadAllBytes(assemblyLocation)),
                        new ReaderParameters(ReadingMode.Immediate)
                        {
                            ReadSymbols = true,
                            ThrowIfSymbolsAreNotMatching = true,
                            SymbolReaderProvider = new PortablePdbReaderProvider(),
                            AssemblyResolver = new OnDemandResolver(),
                            SymbolStream = CreatePdbStreamFor(assemblyLocation)
                        }
                );

            if (!assemblyDefinition.MainModule.HasSymbols)
            {
                throw new Exception("NoSymbols");
            }
            return assemblyDefinition;
        }

        private static MemoryStream CreatePdbStreamFor(string assemblyLocation)
        {
            string pdbFilePath = Path.ChangeExtension(assemblyLocation, ".pdb");
            return !File.Exists(pdbFilePath) ? null : new MemoryStream(File.ReadAllBytes(pdbFilePath));
        }
        
        private class OnDemandResolver : IAssemblyResolver
        {
            public void Dispose()
            {
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                Assembly targetAssembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name.Name);
                string assemblyLocation = targetAssembly.Location;
                
                parameters.AssemblyResolver = this;
                parameters.SymbolStream = CreatePdbStreamFor(assemblyLocation);

                return AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(assemblyLocation)), parameters);
            }
        }
    }
}