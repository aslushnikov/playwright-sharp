using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright.Helpers;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace Microsoft.Playwright.Tests
{
    [Parallelizable(ParallelScope.Self)]
    public class PageWaitForNavigationTests : PageTestEx
    {
        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWork()
        {
            await Page.GotoAsync(Server.EmptyPage);
            var waitForNavigationResult = Page.WaitForNavigationAsync();
            await TaskUtils.WhenAll(
                waitForNavigationResult,
                Page.EvaluateAsync("url => window.location.href = url", Server.Prefix + "/grid.html")
            );
            var response = await waitForNavigationResult;
            Assert.AreEqual((int)HttpStatusCode.OK, response.Status);
            StringAssert.Contains("grid.html", response.Url);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should respect timeout")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldRespectTimeout()
        {
            var waitForNavigationResult = Page.WaitForNavigationAsync(new() { UrlString = "**/frame.html", Timeout = 5000 });

            await Page.GotoAsync(Server.EmptyPage);

            var exception = await PlaywrightAssert.ThrowsAsync<TimeoutException>(() => waitForNavigationResult);

            StringAssert.Contains("Timeout 5000ms exceeded", exception.Message);
            StringAssert.Contains("waiting for navigation to \"**/frame.html\" until \"Load\"", exception.Message);
            StringAssert.Contains($"navigated to \"{Server.EmptyPage}\"", exception.Message);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with both domcontentloaded and load")]
        [Test, Timeout(TestConstants.DefaultTestTimeout), Ignore("Flakes")]
        public async Task NavShouldWorkWithBothDomcontentloadedAndLoad()
        {
            var responseCompleted = new TaskCompletionSource<bool>();
            Server.SetRoute("/one-style.css", _ => responseCompleted.Task);

            var waitForRequestTask = Server.WaitForRequest("/one-style.css");
            var navigationTask = Page.GotoAsync(Server.Prefix + "/one-style.html");
            var domContentLoadedTask = Page.WaitForNavigationAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            bool bothFired = false;
            var bothFiredTask = TaskUtils.WhenAll(
                domContentLoadedTask,
                Page.WaitForNavigationAsync(new() { WaitUntil = WaitUntilState.Load })).ContinueWith(_ => bothFired = true);

            await waitForRequestTask;
            await domContentLoadedTask;
            Assert.False(bothFired);
            responseCompleted.SetResult(true);
            await bothFiredTask;
            await navigationTask;
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with clicking on anchor links")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWithClickingOnAnchorLinks()
        {
            await Page.GotoAsync(Server.EmptyPage);
            await Page.SetContentAsync("<a href='#foobar'>foobar</a>");
            var navigationTask = Page.WaitForNavigationAsync();
            await TaskUtils.WhenAll(
                navigationTask,
                Page.ClickAsync("a")
            );
            Assert.Null(await navigationTask);
            Assert.AreEqual(Server.EmptyPage + "#foobar", Page.Url);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with clicking on links which do not commit navigation")]
        // [Test, Timeout(TestConstants.DefaultTestTimeout)]
        [Test, Ignore("Fix me #1058")]
        public async Task ShouldWorkWithClickingOnLinksWhichDoNotCommitNavigation()
        {
            await Page.GotoAsync(Server.EmptyPage);
            await Page.SetContentAsync($"<a href='{HttpsServer.Prefix}/empty.html'>foobar</a>");
            var navigationTask = Page.WaitForNavigationAsync();
            var exception = await PlaywrightAssert.ThrowsAsync<PlaywrightException>(() => TaskUtils.WhenAll(
                navigationTask,
                Page.ClickAsync("a")
            ));
            TestUtils.AssertSSLError(exception.Message);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with history.pushState()")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWithHistoryPushState()
        {
            await Page.GotoAsync(Server.EmptyPage);
            await Page.SetContentAsync(@"
              <a onclick='javascript:pushState()'>SPA</a>
              <script>
                function pushState() { history.pushState({}, '', 'wow.html') }
              </script>
            ");
            var navigationTask = Page.WaitForNavigationAsync();
            await TaskUtils.WhenAll(
                navigationTask,
                Page.ClickAsync("a")
            );
            Assert.Null(await navigationTask);
            Assert.AreEqual(Server.Prefix + "/wow.html", Page.Url);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with history.replaceState()")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWithHistoryReplaceState()
        {
            await Page.GotoAsync(Server.EmptyPage);
            await Page.SetContentAsync(@"
              <a onclick='javascript:pushState()'>SPA</a>
              <script>
                function pushState() { history.pushState({}, '', 'replaced.html') }
              </script>
            ");
            var navigationTask = Page.WaitForNavigationAsync();
            await TaskUtils.WhenAll(
                navigationTask,
                Page.ClickAsync("a")
            );
            Assert.Null(await navigationTask);
            Assert.AreEqual(Server.Prefix + "/replaced.html", Page.Url);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with DOM history.back()/history.forward()")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWithDOMHistoryBackAndHistoryForward()
        {
            await Page.GotoAsync(Server.EmptyPage);
            await Page.SetContentAsync(@"
              <a id=back onclick='javascript:goBack()'>back</a>
              <a id=forward onclick='javascript:goForward()'>forward</a>
              <script>
                function goBack() { history.back(); }
                function goForward() { history.forward(); }
                history.pushState({}, '', '/first.html');
                history.pushState({}, '', '/second.html');
              </script>
            ");
            Assert.AreEqual(Server.Prefix + "/second.html", Page.Url);
            var navigationTask = Page.WaitForNavigationAsync();
            await TaskUtils.WhenAll(
                navigationTask,
                Page.ClickAsync("a#back")
            );
            Assert.Null(await navigationTask);
            Assert.AreEqual(Server.Prefix + "/first.html", Page.Url);
            navigationTask = Page.WaitForNavigationAsync();
            await TaskUtils.WhenAll(
                navigationTask,
                Page.ClickAsync("a#forward")
            );
            Assert.Null(await navigationTask);
            Assert.AreEqual(Server.Prefix + "/second.html", Page.Url);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work when subframe issues window.stop()")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWhenSubframeIssuesWindowStop()
        {
            //This test is slightly different from the one in PW because of .NET Threads (or thanks to .NET Threads)
            var framesNavigated = new List<IFrame>();
            IFrame frame = null;

            var frameAttachedTaskSource = new TaskCompletionSource<IFrame>();
            Page.FrameAttached += (_, e) =>
            {
                frameAttachedTaskSource.SetResult(e);
            };
            var frameNavigatedTaskSource = new TaskCompletionSource<bool>();
            Page.FrameNavigated += (_, e) =>
            {
                if (frame != null)
                {
                    if (e == frame)
                    {
                        frameNavigatedTaskSource.TrySetResult(true);
                    }
                }
                else
                {
                    framesNavigated.Add(frame);
                }
            };

            Server.SetRoute("/frames/style.css", _ => Task.CompletedTask);
            var navigationTask = Page.GotoAsync(Server.Prefix + "/frames/one-frame.html");

            frame = await frameAttachedTaskSource.Task;

            if (framesNavigated.Contains(frame))
            {
                frameNavigatedTaskSource.TrySetResult(true);
            }

            await frameNavigatedTaskSource.Task;
            await TaskUtils.WhenAll(
                frame.EvaluateAsync("() => window.stop()"),
                navigationTask
            );
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with url match")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWithUrlMatch()
        {
            IResponse response1 = null;
            var response1Task = Page.WaitForNavigationAsync(new() { UrlRegex = new Regex("one-style\\.html") }).ContinueWith(t => response1 = t.Result);
            IResponse response2 = null;
            var response2Task = Page.WaitForNavigationAsync(new() { UrlRegex = new Regex("\\/frame.html") }).ContinueWith(t => response2 = t.Result);
            IResponse response3 = null;
            var response3Task = Page.WaitForNavigationAsync(new()
            {
                UrlFunc = (url) =>
                {
                    var query = new Uri(url).Query.ParseQueryString();
                    return query.ContainsKey("foo") && query["foo"] == "bar";
                }
            }).ContinueWith(t => response3 = t.Result);

            Assert.Null(response1);
            Assert.Null(response2);
            Assert.Null(response3);
            await Page.GotoAsync(Server.EmptyPage);
            Assert.Null(response1);
            Assert.Null(response2);
            Assert.Null(response3);
            await Page.GotoAsync(Server.Prefix + "/frame.html");
            Assert.Null(response1);
            await response2Task;
            Assert.NotNull(response2);
            Assert.Null(response3);
            await Page.GotoAsync(Server.Prefix + "/one-style.html");
            await response1Task;
            Assert.NotNull(response1);
            Assert.NotNull(response2);
            Assert.Null(response3);
            await Page.GotoAsync(Server.Prefix + "/frame.html?foo=bar");
            await response3Task;
            Assert.NotNull(response1);
            Assert.NotNull(response2);
            Assert.NotNull(response3);
            await Page.GotoAsync(Server.EmptyPage);
            Assert.AreEqual(Server.Prefix + "/one-style.html", response1.Url);
            Assert.AreEqual(Server.Prefix + "/frame.html", response2.Url);
            Assert.AreEqual(Server.Prefix + "/frame.html?foo=bar", response3.Url);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work with url match for same document navigations")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkWithUrlMatchForSameDocumentNavigations()
        {
            await Page.GotoAsync(Server.EmptyPage);
            bool resolved = false;
            var waitTask = Page.WaitForNavigationAsync(new() { UrlRegex = new Regex("third\\.html") }).ContinueWith(_ => resolved = true);

            Assert.False(resolved);

            await Page.EvaluateAsync("() => history.pushState({}, '', '/first.html')");
            Assert.False(resolved);

            await Page.EvaluateAsync("() => history.pushState({}, '', '/second.html')");
            Assert.False(resolved);

            await Page.EvaluateAsync("() => history.pushState({}, '', '/third.html')");
            await waitTask;
            Assert.True(resolved);
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work for cross-process navigations")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkForCrossProcessNavigations()
        {
            await Page.GotoAsync(Server.EmptyPage);
            var waitTask = Page.WaitForNavigationAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            string url = Server.CrossProcessPrefix + "/empty.html";
            var gotoTask = Page.GotoAsync(url);
            var response = await waitTask;
            Assert.AreEqual(url, response.Url);
            Assert.AreEqual(url, Page.Url);
            Assert.AreEqual(url, await Page.EvaluateAsync<string>("document.location.href"));
            await gotoTask;
        }

        [PlaywrightTest("page-wait-for-navigation.spec.ts", "should work on frame")]
        [Test, Timeout(TestConstants.DefaultTestTimeout)]
        public async Task ShouldWorkOnFrame()
        {
            await Page.GotoAsync(Server.Prefix + "/frames/one-frame.html");
            var frame = Page.Frames.ElementAt(1);
            var (response, _) = await TaskUtils.WhenAll(
                frame.WaitForNavigationAsync(),
                frame.EvaluateAsync("url => window.location.href = url", Server.Prefix + "/grid.html")
            );
            Assert.AreEqual((int)HttpStatusCode.OK, response.Status);
            StringAssert.Contains("grid.html", response.Url);
            Assert.AreEqual(frame, response.Frame);
            StringAssert.Contains("/frames/one-frame.html", Page.Url);
        }
    }
}
