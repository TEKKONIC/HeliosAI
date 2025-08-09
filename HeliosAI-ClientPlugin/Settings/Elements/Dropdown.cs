using Sandbox.Graphics.GUI;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ClientPlugin.Settings.Elements
{
    internal class DropdownAttribute : Attribute, IElement
    {
        public readonly int VisibleRows;
        public readonly string Label;
        public readonly string Description;

        private static string UnCamelCase(string str)
        {
            return Regex.Replace(
                Regex.Replace(
                    str,
                    @"(\P{Ll})(\P{Ll}\p{Ll})",
                    "$1 $2"
                ),
                @"(\p{Ll})(\P{Ll})",
                "$1 $2"
            );
        }

        public DropdownAttribute(int visibleRows = 20, string label = null, string description = null)
        {
            VisibleRows = visibleRows;
            Label = label;
            Description = description;
        }

        public List<Control> GetControls(string name, Func<object> propertyGetter, Action<object> propertySetter)
        {
            var selectedEnum = propertyGetter();
            var choiceEnum = selectedEnum.GetType();

            var dropdown = new MyGuiControlCombobox(toolTip: Description);
            var elements = Enum.GetNames(choiceEnum);

            for (var i = 0; i < elements.Length; i++)
            {
                dropdown.AddItem(i, UnCamelCase(elements[i]));
            }

            void OnItemSelect()
            {
                var key = dropdown.GetSelectedKey();
                var value = elements[key];

                var enumValue = Enum.Parse(choiceEnum, value);
                propertySetter(enumValue);
            }

            dropdown.ItemSelected += OnItemSelect;
            dropdown.SelectItemByIndex(Convert.ToInt32(selectedEnum));

            var label = Tools.GetLabelOrDefault(name, Label);
            return new List<Control>()
            {
                new Control(new MyGuiControlLabel(text: label), minWidth: Control.LabelMinWidth),
                new Control(dropdown, fillFactor: 1f),
            };
        }
        public List<Type> SupportedTypes { get; } = new List<Type>()
        {
            typeof(Enum)
        };
    }
}
