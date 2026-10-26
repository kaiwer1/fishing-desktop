using System;

namespace FishingDesktop.Diagnostics.HostPolicy
{
    public sealed class DrawProgressGuard
    {
        string document;
        long revision=-1,frames=-1;
        public int Checks {get;private set;}
        public void Observe(bool comparable,string currentDocument,long currentRevision,long currentFrames,bool running)
        {
            if(!comparable){document=null;revision=frames=-1;return;}
            if(String.IsNullOrEmpty(currentDocument) || currentRevision<0 || currentFrames<0)throw new InvalidOperationException("Invalid draw progress sample.");
            if(!running)throw new InvalidOperationException("Active diagnostic draw loop stopped.");
            if(document==currentDocument && revision==currentRevision){
                if(currentFrames<=frames)throw new InvalidOperationException("Active diagnostic view made no draw progress in a stable interval.");
                Checks++;
            }
            document=currentDocument;revision=currentRevision;frames=currentFrames;
        }
    }
}
