using System;
using System.Drawing;
using System.Windows.Forms;

// One bitmap per owned read-only view, always sourced from its own WebView.
// Never captures desktop pixels or saves the image to disk.
sealed class FrozenFrameSurface : IDisposable
{
    readonly Control view;
    readonly PictureBox backdrop;
    readonly EventHandler resized;
    Bitmap image;
    bool disposed;
    internal bool HasImage {get{return image!=null;}}
    internal bool IsShown {get{return !disposed && backdrop.Visible && image!=null;}}
    internal Size ImageSize {get{return image==null?Size.Empty:image.Size;}}
    internal FrozenFrameSurface(Control view)
    {
        this.view=view;
        backdrop=new PictureBox{Visible=false,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(24,48,53),Bounds=view.Bounds};
        view.Parent.Controls.Add(backdrop);view.BringToFront();
        resized=delegate{if(!disposed)backdrop.Bounds=view.Bounds;};view.SizeChanged+=resized;view.LocationChanged+=resized;
    }
    internal void Assign(Bitmap next)
    {
        if(disposed){next.Dispose();return;}
        var previous=image;image=next;backdrop.Image=next;
        if(previous!=null)previous.Dispose();
    }
    internal void Show(){if(!disposed && image!=null){backdrop.Bounds=view.Bounds;backdrop.Visible=true;backdrop.BringToFront();}}
    internal void Hide(){if(!disposed)backdrop.Visible=false;}
    public void Dispose()
    {
        if(disposed)return;disposed=true;view.SizeChanged-=resized;view.LocationChanged-=resized;
        backdrop.Image=null;backdrop.Dispose();if(image!=null){image.Dispose();image=null;}
    }
}
