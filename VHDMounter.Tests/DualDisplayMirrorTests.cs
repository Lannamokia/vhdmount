using System;
using System.Collections.Generic;
using Xunit;

namespace VHDMounter.Tests
{
    public sealed class DualDisplayMirrorTests
    {
        [Fact]
        public void TargetSelectorChoosesTheOtherMatchingPortraitMonitor()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);
            var landscape = new DisplayMonitorBounds("DISPLAY2", 1080, 0, 1920, 1080, false);
            var matchingPortrait = new DisplayMonitorBounds("DISPLAY3", -1080, 0, 1080, 1920, false);

            var selected = DisplayMirrorTargetSelector.SelectSecondary(
                new[] { main, landscape, matchingPortrait },
                main);

            Assert.Equal(matchingPortrait, selected);
            Assert.Equal(-1080, selected?.Left);
        }

        [Fact]
        public void TargetSelectorReturnsNullForSingleMonitor()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);

            var selected = DisplayMirrorTargetSelector.SelectSecondary(new[] { main }, main);

            Assert.Null(selected);
        }

        [Fact]
        public void CoordinatorCreatesOneMirrorAndAvoidsDuplicateShowCalls()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);
            var secondary = new DisplayMonitorBounds("DISPLAY2", 1080, 0, 1080, 1920, false);
            var provider = new FakeMonitorProvider(main, new[] { main, secondary });
            var mirror = new FakeMirrorWindow();
            var factoryCalls = 0;
            using var coordinator = new DualDisplayMirrorCoordinator(provider, () =>
            {
                factoryCalls++;
                return mirror;
            });

            coordinator.Refresh(new IntPtr(42), shouldShow: true);
            coordinator.Refresh(new IntPtr(42), shouldShow: true);

            Assert.Equal(1, factoryCalls);
            Assert.Equal(1, mirror.ShowCalls);
            Assert.True(mirror.IsVisible);
            Assert.Equal(secondary, mirror.LastMonitor);
        }

        [Fact]
        public void CoordinatorHidesMirrorWhenGameHidesMainSurface()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);
            var secondary = new DisplayMonitorBounds("DISPLAY2", 1080, 0, 1080, 1920, false);
            var provider = new FakeMonitorProvider(main, new[] { main, secondary });
            var mirror = new FakeMirrorWindow();
            using var coordinator = new DualDisplayMirrorCoordinator(provider, () => mirror);

            coordinator.Refresh(new IntPtr(42), shouldShow: true);
            coordinator.Refresh(new IntPtr(42), shouldShow: false);

            Assert.False(mirror.IsVisible);
            Assert.Equal(1, mirror.HideCalls);
        }

        [Fact]
        public void CoordinatorReShowsMirrorWhenBaseWindowBecomesVisibleAgain()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);
            var secondary = new DisplayMonitorBounds("DISPLAY2", 1080, 0, 1080, 1920, false);
            var provider = new FakeMonitorProvider(main, new[] { main, secondary });
            var mirror = new FakeMirrorWindow();
            using var coordinator = new DualDisplayMirrorCoordinator(provider, () => mirror);

            coordinator.Refresh(new IntPtr(42), shouldShow: true);
            coordinator.Refresh(new IntPtr(42), shouldShow: false);
            coordinator.Refresh(new IntPtr(42), shouldShow: true);

            Assert.True(mirror.IsVisible);
            Assert.Equal(2, mirror.ShowCalls);
            Assert.Equal(1, mirror.HideCalls);
            Assert.Equal(secondary, mirror.LastMonitor);
        }

        [Fact]
        public void CoordinatorHidesMirrorWhenSecondaryMonitorIsRemoved()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);
            var secondary = new DisplayMonitorBounds("DISPLAY2", 1080, 0, 1080, 1920, false);
            var provider = new FakeMonitorProvider(main, new[] { main, secondary });
            var mirror = new FakeMirrorWindow();
            using var coordinator = new DualDisplayMirrorCoordinator(provider, () => mirror);

            coordinator.Refresh(new IntPtr(42), shouldShow: true);
            provider.Monitors = new[] { main };
            coordinator.Refresh(new IntPtr(42), shouldShow: true);

            Assert.False(mirror.IsVisible);
            Assert.Equal(1, mirror.HideCalls);
        }

        [Fact]
        public void CoordinatorRepositionsExistingMirrorWhenTopologyChanges()
        {
            var main = new DisplayMonitorBounds("DISPLAY1", 0, 0, 1080, 1920, true);
            var secondary = new DisplayMonitorBounds("DISPLAY2", 1080, 0, 1080, 1920, false);
            var movedSecondary = new DisplayMonitorBounds("DISPLAY2", -1080, 0, 1080, 1920, false);
            var provider = new FakeMonitorProvider(main, new[] { main, secondary });
            var mirror = new FakeMirrorWindow();
            using var coordinator = new DualDisplayMirrorCoordinator(provider, () => mirror);

            coordinator.Refresh(new IntPtr(42), shouldShow: true);
            provider.Monitors = new[] { main, movedSecondary };
            coordinator.Refresh(new IntPtr(42), shouldShow: true);

            Assert.Equal(2, mirror.ShowCalls);
            Assert.Equal(movedSecondary, mirror.LastMonitor);
        }

        private sealed class FakeMonitorProvider : IDisplayMonitorProvider
        {
            private readonly DisplayMonitorBounds mainMonitor;

            public FakeMonitorProvider(DisplayMonitorBounds mainMonitor, IReadOnlyList<DisplayMonitorBounds> monitors)
            {
                this.mainMonitor = mainMonitor;
                Monitors = monitors;
            }

            public IReadOnlyList<DisplayMonitorBounds> Monitors { get; set; }

            public IReadOnlyList<DisplayMonitorBounds> GetMonitors()
            {
                return Monitors;
            }

            public DisplayMonitorBounds? GetMonitorForWindow(IntPtr windowHandle)
            {
                return mainMonitor;
            }
        }

        private sealed class FakeMirrorWindow : ISecondaryDisplayMirrorWindow
        {
            public bool IsVisible { get; private set; }

            public int ShowCalls { get; private set; }

            public int HideCalls { get; private set; }

            public bool Disposed { get; private set; }

            public DisplayMonitorBounds? LastMonitor { get; private set; }

            DisplayMonitorBounds ISecondaryDisplayMirrorWindow.CurrentMonitor => LastMonitor;

            public void ShowOn(DisplayMonitorBounds monitor)
            {
                LastMonitor = monitor;
                ShowCalls++;
                IsVisible = true;
            }

            public void Hide()
            {
                HideCalls++;
                IsVisible = false;
            }

            public void Dispose()
            {
                Disposed = true;
                IsVisible = false;
            }
        }
    }
}
