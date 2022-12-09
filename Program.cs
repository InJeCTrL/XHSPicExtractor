const string shareLinkPrefix = "http://xhslink.com";
const string pageSplitPattern = "class=\"note-image\"";
const string hcaptchaURL = "https://hcaptcha.com/siteverify";

var app = WebApplication.Create(args);

#region HCaptcha client
string hcSecret = app.Configuration.GetValue<string>("HCaptchaSecret") ?? "";
var hcaptchaClient = new HttpClient();
#endregion

#region 小红书 client
var xhsClient = new HttpClient(new HttpClientHandler
{
    AllowAutoRedirect = false
});
xhsClient.DefaultRequestHeaders.Add("User-Agent", app.Configuration.GetValue<string>("UserAgent") ?? "");
xhsClient.DefaultRequestHeaders.Add("Cookie", app.Configuration.GetValue<string>("Cookie") ?? "");
#endregion

app.Use(async (context, next) =>
{
    bool hasHCT = context.Request.Headers.TryGetValue("h-captcha-response", out var hCTResponse);
    if (hasHCT)
    {
        var postData = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("response", hCTResponse.ToString()),
            new KeyValuePair<string, string>("secret", hcSecret)
        };

        using var response = await hcaptchaClient.PostAsync(hcaptchaURL, new FormUrlEncodedContent(postData));
        HCaptchaReturn verifyResult = (await response.Content.ReadFromJsonAsync<HCaptchaReturn>()) ?? new HCaptchaReturn();
        if (verifyResult.success)
        {
            await next.Invoke();
            return;
        }
    }

    await Results.Json<ReturnPattern>(new ReturnPattern
    {
        msg = "验证码校验失败"
    }).ExecuteAsync(context);
});

app.MapPost("/imgs", async (context) =>
{
    using var body = new StreamReader(context.Request.Body);
    string target = await body.ReadToEndAsync();
    var pageUrl = target;

    if (target.Contains(shareLinkPrefix))
    {
        var shortUrl = target[target.IndexOf(shareLinkPrefix)..];
        shortUrl = shortUrl[..(shortUrl.IndexOf(pageSplitPattern) + pageSplitPattern.Length + 7)];

        using var response = await xhsClient.GetAsync(shortUrl);
        if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
        response.StatusCode == System.Net.HttpStatusCode.RedirectMethod ||
        response.StatusCode == System.Net.HttpStatusCode.RedirectKeepVerb)
        {
            var redirectLocation = response.Headers.Location;
            if (redirectLocation == null)
            {
                await Results.Json<ReturnPattern>(new ReturnPattern
                {
                    msg = "分享链接有误"
                }).ExecuteAsync(context);

                return;
            }

            pageUrl = redirectLocation.ToString();
        }
    }

    if (pageUrl.Contains("?"))
    {
        pageUrl = pageUrl[..pageUrl.IndexOf("?")];
    }

    var pageContent = await xhsClient.GetStringAsync(pageUrl);

    if (!pageContent.Contains("xiaohongshu.com"))
    {
        await Results.Json<ReturnPattern>(new ReturnPattern
        {
            msg = "不是有效的小红书链接"
        }).ExecuteAsync(context);

        return;
    }

    string[] slices = pageContent.Split(pageSplitPattern, StringSplitOptions.RemoveEmptyEntries)[..^1];
    var urls = new List<string>();
    foreach (string slice in slices)
    {
        var img_url = slice[(slice.LastIndexOf("src=\"") + 5)..];
        img_url = img_url[..img_url.IndexOf("?")];
        urls.Add(img_url);
    }

    if (urls.Count == 0)
    {
        await Results.Json<ReturnPattern>(new ReturnPattern
        {
            msg = "没有解析到原图"
        }).ExecuteAsync(context);

        return;
    }

    await Results.Json<ReturnPattern>(new ReturnPattern
    {
        success = 1,
        urls = urls
    }).ExecuteAsync(context);
});

app.Run();

public class HCaptchaReturn
{
    public bool success { get; set; }
}

public class ReturnPattern
{
    public int success { get; set; } = 0;
    public List<string> urls { get; set; } = new List<string>();
    public string msg { get; set; } = string.Empty;
}