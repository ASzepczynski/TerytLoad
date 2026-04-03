using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TerytLoad.Pages.DbViewer
{
    public class IndexModel : PageModel
    {
        public List<ViewerConfig> Viewers { get; set; } = new();

        public void OnGet()
        {
            Viewers = ViewerRegistry.GetAllConfigs().ToList();
        }
    }
}