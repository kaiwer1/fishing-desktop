using System;

// One explicit activation request may authorize one current, focused document.
sealed class ActivationHandshake
{
    string request;
    string view;
    string document;
    int generation;
    long runRevision;
    string issuedRequest;
    string issuedView;
    public string Begin(string viewId,string documentId,int navigationGeneration,long currentRunRevision=0)
    {
        view=viewId;document=documentId;generation=navigationGeneration;runRevision=currentRunRevision;
        return request=Guid.NewGuid().ToString("D");
    }
    public void Cancel(string viewId) { if(view==viewId)request=null;if(issuedView==viewId)issuedRequest=null; }
    public bool PageCancelled(string viewId,string requestId)
    {
        if(String.IsNullOrEmpty(requestId))return false;
        if(view==viewId && request==requestId)request=null;
        if(issuedView!=viewId || issuedRequest!=requestId)return false;
        issuedRequest=null;return true;
    }
    public bool Complete(string requestId,string viewId,string documentId,int navigationGeneration,bool focused,int epoch,int minimumEpoch,long currentRunRevision=0)
    {
        if(request==null || requestId!=request)return false;
        request=null; // A matching acknowledgement is consumed even when rejected.
        bool accepted=view==viewId && document==documentId && generation==navigationGeneration && runRevision==currentRunRevision && focused && epoch>=0 && epoch>=minimumEpoch;
        if(accepted){issuedRequest=requestId;issuedView=viewId;}
        return accepted;
    }
    public static bool CurrentNavigation(ulong expectedNavigation,int expectedGeneration,ulong navigation,int currentGeneration,bool awaitingStart)
    {
        return !awaitingStart && expectedNavigation==navigation && expectedGeneration==currentGeneration;
    }
}
