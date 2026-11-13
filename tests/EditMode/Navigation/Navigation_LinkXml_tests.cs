using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.Navigation
{
    /// <summary>
    /// Navigation Story 009: <c>Assets/link.xml</c> preserves <c>UnityEngine.AI</c> (ADR-002 Decision 5b) and
    /// <c>UnityEngine.SceneManagement</c> (ADR-009 step 6), and still keeps the database libraries.
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_LinkXml_Tests
    {
        private XDocument _doc;

        [SetUp]
        public void SetUp()
        {
            string path = Path.Combine(Application.dataPath, "link.xml");
            Assert.That(File.Exists(path), "Assets/link.xml not found at " + path);
            _doc = XDocument.Load(path);
        }

        private bool HasNamespacePreserve(string assemblyName, string namespaceName)
        {
            return _doc.Descendants("assembly")
                .Where(a => (string)a.Attribute("fullname") == assemblyName)
                .SelectMany(a => a.Elements("namespace"))
                .Any(n => (string)n.Attribute("fullname") == namespaceName && (string)n.Attribute("preserve") == "all");
        }

        private bool HasAssemblyPreserve(string assemblyName)
        {
            return _doc.Descendants("assembly")
                .Any(a => (string)a.Attribute("fullname") == assemblyName && (string)a.Attribute("preserve") == "all");
        }

        [Test]
        public void test_link_xml_preserves_unity_engine_ai()
        {
            Assert.That(HasNamespacePreserve("UnityEngine.AIModule", "UnityEngine.AI"), Is.True);
        }

        [Test]
        public void test_link_xml_preserves_scene_management()
        {
            Assert.That(HasNamespacePreserve("UnityEngine.CoreModule", "UnityEngine.SceneManagement"), Is.True);
        }

        [Test]
        public void test_link_xml_keeps_npgsql_entry()
        {
            Assert.That(HasAssemblyPreserve("Npgsql"), Is.True);
        }

        [Test]
        public void test_link_xml_keeps_dapper_entry()
        {
            Assert.That(HasAssemblyPreserve("Dapper"), Is.True);
        }
    }
}
