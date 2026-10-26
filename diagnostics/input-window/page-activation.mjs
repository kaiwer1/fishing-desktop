export class PageActivation {
  constructor(documentId,session){this.documentId=documentId;this.session=session;this.pending=null;this.issued=null;}
  prepare(message,{focused=false}={}){
    if(!message || message.kind!=='prepare-input' || message.documentId!==this.documentId || typeof message.requestId!=='string' ||
      !/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(message.requestId))return null;
    this.pending=focused===true?message.requestId:null;
    return {requestId:message.requestId,cancellationEpoch:this.session.cancellationEpoch,focused:focused===true};
  }
  matches(message){return Boolean(this.pending && message && message.documentId===this.documentId && message.activationRequestId===this.pending && message.cancellationEpoch===this.session.cancellationEpoch);}
  applied(message){if(message.enabled){this.issued=this.pending;this.pending=null;}else this.cancel();}
  cancel(){const request=this.pending??this.issued??'';this.pending=null;this.issued=null;return request;}
}
