using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MCPForUnityTests.Editor;

namespace MCPForUnityTests.Editor.Tools
{
    public class AssetReferenceSetPropertyTests
    {
        private const string TempRoot = "Assets/Temp/AssetReferenceSetPropertyTests";
        private string _texturePath;
        private string _fontPath;
        private string _materialPath;
        private string _prefabPath;
        private GameObject _testObject;

        [SetUp]
        public void SetUp()
        {
            TestUtilities.EnsureFolder(TempRoot);
            _texturePath = $"{TempRoot}/Atlas.asset";
            _fontPath = $"{TempRoot}/TempFont.asset";
            _materialPath = $"{TempRoot}/TempMaterial.mat";
            _prefabPath = $"{TempRoot}/ReferencePrefab.prefab";

            CleanupAssets();
            CreateAssets();

            _testObject = new GameObject("AssetReferenceSetProperty_Target");
            CommandRegistry.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_testObject);
            }

            CleanupAssets();
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            AssetDatabase.Refresh();
        }

        [Test]
        public void ManageComponents_SetProperty_ImageSprite_WithCanonicalSubAssetReference()
        {
            _testObject.AddComponent<Image>();
            var spriteGuid = AssetDatabase.AssetPathToGUID(_texturePath);

            var result = ManageComponents.HandleCommand(new JObject
            {
                ["action"] = "set_property",
                ["target"] = _testObject.name,
                ["componentType"] = "Image",
                ["property"] = "sprite",
                ["value"] = new JObject
                {
                    ["type"] = "AssetReference",
                    ["guid"] = spriteGuid,
                    ["path"] = _texturePath,
                    ["subAsset"] = "Sprite:Icon"
                }
            });

            JObject resultObject = TestUtilities.ToJObject(result);
            Assert.IsTrue(resultObject.Value<bool>("success"), resultObject.ToString());

            var image = _testObject.GetComponent<Image>();
            Assert.IsNotNull(image.sprite);
            Assert.AreEqual("Icon", image.sprite.name);
        }

        [Test]
        public void ComponentOps_SetProperty_PrefabField_WithLegacyGuidObject()
        {
            var holder = _testObject.AddComponent<TestNamespace.PrefabReferenceHolder>();
            var prefabGuid = AssetDatabase.AssetPathToGUID(_prefabPath);
            var token = new JObject { ["guid"] = prefabGuid };

            bool success = ComponentOps.SetProperty(holder, "prefabReference", token, out string error);

            Assert.IsTrue(success, error);
            Assert.IsNotNull(holder.prefabReference);
            Assert.AreEqual("ReferencePrefab", holder.prefabReference.name);
        }

        [Test]
        public void ComponentOps_SetProperty_ImageSprite_TypeMismatch_ReturnsDetailedError()
        {
            var image = _testObject.AddComponent<Image>();
            var token = new JObject
            {
                ["type"] = "AssetReference",
                ["path"] = _materialPath
            };

            bool success = ComponentOps.SetProperty(image, "sprite", token, out string error);

            Assert.IsFalse(success);
            StringAssert.Contains("Resolved asset", error);
            StringAssert.Contains("Material", error);
            StringAssert.Contains("Sprite", error);
        }

        [Test]
        public void ComponentOps_SetProperty_TmpTextFont_And_FontSharedMaterial_WithAssetReference()
        {
            Type tmpTextType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpTextType == null)
            {
                Assert.Ignore("TMP package not present in this test environment.");
                return;
            }

            var tmpText = _testObject.AddComponent(tmpTextType) as Component;
            Assert.IsNotNull(tmpText);

            var fontGuid = AssetDatabase.AssetPathToGUID(_fontPath);
            bool fontSuccess = ComponentOps.SetProperty(tmpText, "font", new JObject
            {
                ["type"] = "AssetReference",
                ["guid"] = fontGuid,
                ["path"] = _fontPath
            }, out string fontError);

            Assert.IsTrue(fontSuccess, fontError);

            bool materialSuccess = ComponentOps.SetProperty(tmpText, "fontSharedMaterial", new JObject
            {
                ["type"] = "AssetReference",
                ["path"] = _materialPath
            }, out string materialError);

            Assert.IsTrue(materialSuccess, materialError);
            Assert.NotNull(tmpTextType.GetProperty("font")?.GetValue(tmpText));
            Assert.NotNull(tmpTextType.GetProperty("fontSharedMaterial")?.GetValue(tmpText));
        }

        private void CreateAssets()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "Atlas" };
            AssetDatabase.CreateAsset(texture, _texturePath);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f));
            sprite.name = "Icon";
            AssetDatabase.AddObjectToAsset(sprite, _texturePath);

            Shader shader = TestUtilities.FindFallbackShader();
            Assert.NotNull(shader, "Could not find fallback shader for material creation.");
            AssetDatabase.CreateAsset(new Material(shader) { name = "TempFontMaterial" }, _materialPath);

            Type fontAssetType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro");
            if (fontAssetType != null)
            {
                var fontAsset = ScriptableObject.CreateInstance(fontAssetType);
                fontAsset.name = "TempFont";
                AssetDatabase.CreateAsset(fontAsset, _fontPath);
            }

            var prefabSource = new GameObject("ReferencePrefab");
            PrefabUtility.SaveAsPrefabAsset(prefabSource, _prefabPath);
            UnityEngine.Object.DestroyImmediate(prefabSource);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void CleanupAssets()
        {
            TestUtilities.SafeDeleteAsset(_texturePath);
            TestUtilities.SafeDeleteAsset(_fontPath);
            TestUtilities.SafeDeleteAsset(_materialPath);
            TestUtilities.SafeDeleteAsset(_prefabPath);
            AssetDatabase.SaveAssets();
        }
    }
}
