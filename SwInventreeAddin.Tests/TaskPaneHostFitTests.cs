using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Drives <see cref="TaskPaneHostFit"/> against real HWNDs: a WinForms child
    /// parented inside a larger control reproduces the same geometry misfit
    /// SolidWorks leaves behind (issue #307), without needing a SolidWorks host.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class TaskPaneHostFitTests
    {
        [Test]
        public void EnsureHostFit_ExpandsUndersizedChild_ToParentClientArea()
        {
            using var parent = CreateParent(500, 400);
            var child = AddChild(parent, 150, 150);

            TaskPaneHostFit.EnsureHostFit(child);

            Assert.That(child.Bounds, Is.EqualTo(parent.ClientRectangle));
        }

        [Test]
        public void EnsureHostFit_ShrinksOversizedChild_ToParentClientArea()
        {
            using var parent = CreateParent(300, 200);
            var child = AddChild(parent, 600, 500);

            TaskPaneHostFit.EnsureHostFit(child);

            Assert.That(child.Bounds, Is.EqualTo(parent.ClientRectangle));
        }

        [Test]
        public void EnsureHostFit_OffsetChild_SnapsToParentOrigin()
        {
            using var parent = CreateParent(500, 400);
            var child = AddChild(parent, 150, 150);
            child.Location = new Point(30, 40);

            TaskPaneHostFit.EnsureHostFit(child);

            Assert.That(child.Bounds, Is.EqualTo(parent.ClientRectangle));
        }

        [Test]
        public void EnsureHostFit_AlreadyFitted_LeavesBoundsUnchanged()
        {
            using var parent = CreateParent(500, 400);
            var child = AddChild(parent, 500, 400);

            TaskPaneHostFit.EnsureHostFit(child);

            Assert.That(child.Bounds, Is.EqualTo(parent.ClientRectangle));
        }

        [Test]
        public void EnsureHostFit_ZeroSizedParent_IsNoOp()
        {
            using var parent = CreateParent(0, 0);
            var child = AddChild(parent, 150, 150);
            var before = child.Bounds;

            TaskPaneHostFit.EnsureHostFit(child);

            Assert.That(child.Bounds, Is.EqualTo(before));
        }

        [Test]
        public void EnsureHostFit_HandleNotCreated_IsNoOp()
        {
            using var child = new Control { Size = new Size(150, 150) };

            TaskPaneHostFit.EnsureHostFit(child);

            Assert.That(child.IsHandleCreated, Is.False);
        }

        [Test]
        public void Attach_ChildReshown_FitsToParentClientArea()
        {
            using var parent = CreateParent(500, 400);
            var child = AddChild(parent, 150, 150);
            TaskPaneHostFit.Attach(child);

            child.Visible = false;
            child.Visible = true;

            Assert.That(child.Bounds, Is.EqualTo(parent.ClientRectangle));
        }

        private static UserControl CreateParent(int width, int height)
        {
            var parent = new UserControl { Size = new Size(width, height) };
            parent.CreateControl();
            return parent;
        }

        private static Control AddChild(Control parent, int width, int height)
        {
            var child = new Control { Size = new Size(width, height) };
            parent.Controls.Add(child);
            child.CreateControl();
            return child;
        }
    }
}
