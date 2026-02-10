using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace MCPForUnityTests.Editor.Tools
{
    public class ComponentOpsNestedPropertyTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("ComponentOpsNestedPropertyTests");
            CommandRegistry.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void SetProperty_ButtonColorsNestedStructPath_WritesBackStructValue()
        {
            var button = _gameObject.AddComponent<Button>();

            bool success = ComponentOps.SetProperty(button, "colors.normalColor.r", new JValue(0.15f), out string error);

            Assert.IsTrue(success, error);
            Assert.IsTrue(string.IsNullOrEmpty(error), error);
            Assert.AreEqual(0.15f, button.colors.normalColor.r, 0.0001f);
        }

        [Test]
        public void SetProperty_TmpAlignmentEnum_CaseInsensitiveSymbol()
        {
            Type tmpType = UnityTypeResolver.ResolveComponent("TextMeshProUGUI");
            if (tmpType == null)
            {
                Assert.Ignore("TextMeshProUGUI is not available in this project.");
            }

            var tmpComponent = _gameObject.AddComponent(tmpType) as Component;
            Assert.IsNotNull(tmpComponent);

            bool success = ComponentOps.SetProperty(tmpComponent, "alignment", new JValue("bottomright"), out string error);

            Assert.IsTrue(success, error);
            object enumValue = tmpType.GetProperty("alignment")?.GetValue(tmpComponent);
            Assert.IsNotNull(enumValue);
            Assert.AreEqual("BottomRight", enumValue.ToString());
        }

        [Test]
        public void SetProperty_TmpAlignmentEnum_InvalidSymbol_ReturnsClearError()
        {
            Type tmpType = UnityTypeResolver.ResolveComponent("TextMeshProUGUI");
            if (tmpType == null)
            {
                Assert.Ignore("TextMeshProUGUI is not available in this project.");
            }

            var tmpComponent = _gameObject.AddComponent(tmpType) as Component;
            bool success = ComponentOps.SetProperty(tmpComponent, "alignment", new JValue("nope_alignment"), out string error);

            Assert.IsFalse(success);
            StringAssert.Contains("Invalid enum symbol", error);
        }

        [Test]
        public void SetProperty_RendererMaterialArrayIndexedSet_RejectsIncompatibleItemType()
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = primitive.GetComponent<Renderer>();
                Assert.IsNotNull(renderer);

                bool success = ComponentOps.SetProperty(renderer, "materials[0]", new JValue(123), out string error);

                Assert.IsFalse(success);
                StringAssert.Contains("Incompatible collection item type", error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(primitive);
            }
        }

        [Test]
        public void ManageComponents_SetEnabled_TogglesBehaviourAndRenderer()
        {
            _gameObject.AddComponent<BoxCollider>();
            var renderer = _gameObject.AddComponent<MeshRenderer>();
            var behaviour = _gameObject.AddComponent<AudioSource>();

            var parameters = new JObject
            {
                ["action"] = "set_enabled",
                ["target"] = _gameObject.name,
                ["value"] = false
            };

            var result = ManageComponents.HandleCommand(parameters);
            Assert.IsNotNull(result);
            Assert.IsFalse(renderer.enabled);
            Assert.IsFalse(behaviour.enabled);
        }



        [Test]
        public void SetProperty_ArrayProxyIndexedSet_WritesBackThroughPropertySetter()
        {
            var proxy = _gameObject.AddComponent<CollectionProxyComponent>();

            bool success = ComponentOps.SetProperty(proxy, "numbersProxy[1]", new JValue(99), out string error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(99, proxy.Numbers[1]);
        }

        [Test]
        public void SetProperty_ReadOnlyArrayProxyIndexedSet_ReturnsReadOnlyError()
        {
            var proxy = _gameObject.AddComponent<CollectionProxyComponent>();

            bool success = ComponentOps.SetProperty(proxy, "readOnlyNumbersProxy[0]", new JValue(55), out string error);

            Assert.IsFalse(success);
            StringAssert.Contains("read-only", error);
        }

        [Test]
        public void SetProperty_UnknownMemberSegment_ReturnsClearError()
        {
            var button = _gameObject.AddComponent<Button>();

            bool success = ComponentOps.SetProperty(button, "colors.notARealSegment.r", new JValue(0.2f), out string error);

            Assert.IsFalse(success);
            StringAssert.Contains("Unknown member segment", error);
        }
    }

    public class CollectionProxyComponent : MonoBehaviour
    {
        private int[] _numbers = { 1, 2, 3 };

        public int[] NumbersProxy
        {
            get => (int[])_numbers.Clone();
            set => _numbers = value;
        }

        public int[] ReadOnlyNumbersProxy => (int[])_numbers.Clone();

        public int[] Numbers => _numbers;
    }

}
