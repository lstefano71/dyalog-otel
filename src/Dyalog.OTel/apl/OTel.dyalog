⍝ OTel — APL cover namespace for dyalog-otel
⍝ Provides idiomatic APL wrappers around pp_otel_* DWA exports.
⍝
⍝ Usage:
⍝   OTel.Init                          ⍝ Lazy-init singleton
⍝   OTel.Log 9 'Hello world' '' ⍬      ⍝ Info log, no template, no attrs
⍝   OTel.Log 9 'Order {OrderId} placed' '' (42)  ⍝ Structured template
⍝   h←OTel.SpanStart 'myop' 0 '' ⍬    ⍝ Root span → handle
⍝   OTel.SpanEnd h '' ⍬                ⍝ End span
⍝   OTel.Shutdown 0                    ⍝ Shutdown singleton

:Namespace OTel

    ⎕IO←0 ⋄ ⎕ML←1

    ∇ Init
    ⍝ Initialize the singleton pipeline (lazy, loads config).
    ⍝ Returns 0 (singleton handle).
      :Access Public Shared
      'pp_otel_init' ⎕NA 'dyalogotel|pp_otel_init >PP'
      Init←pp_otel_init
    ∇

    ∇ Log args;sev;msg;tpl;attrs;pipeline
    ⍝ Emit a log record.
    ⍝ args: severity message templateName attrs
    ⍝ Left arg (optional): pipeline handle (default 0 = singleton)
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (sev msg tpl attrs)←4↑args,(⍴,args)↓0 '' '' ⍬
      'pp_otel_log' ⎕NA 'dyalogotel|pp_otel_log I4 I4 <PP <PP <PP'
      pp_otel_log pipeline sev msg tpl attrs
    ∇

    ∇ LogSpan args;sev;msg;sh;tpl;attrs;pipeline
    ⍝ Emit a log correlated with an active span.
    ⍝ args: severity message spanHandle templateName attrs
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (sev msg sh tpl attrs)←5↑args,(⍴,args)↓0 '' 0 '' ⍬
      'pp_otel_log_span' ⎕NA 'dyalogotel|pp_otel_log_span I4 I4 <PP I4 <PP <PP'
      pp_otel_log_span pipeline sev msg sh tpl attrs
    ∇

    ∇ h←SpanStart args;name;parent;tpl;attrs;pipeline
    ⍝ Start a span, returns handle.
    ⍝ args: name parentHandle templateName attrs
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (name parent tpl attrs)←4↑args,(⍴,args)↓'' 0 '' ⍬
      'pp_otel_span_start' ⎕NA 'dyalogotel|pp_otel_span_start I4 <PP I4 <PP <PP >PP'
      h←pp_otel_span_start pipeline name parent tpl attrs
    ∇

    ∇ SpanEnd args;sh;tpl;attrs;pipeline
    ⍝ End a span.
    ⍝ args: spanHandle templateName attrs
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (sh tpl attrs)←3↑args,(⍴,args)↓0 '' ⍬
      'pp_otel_span_end' ⎕NA 'dyalogotel|pp_otel_span_end I4 I4 <PP <PP'
      pp_otel_span_end pipeline sh tpl attrs
    ∇

    ∇ Metric args;name;val;mtype;tpl;attrs;pipeline
    ⍝ Record a metric data point.
    ⍝ args: metricName value metricType templateName attrs
    ⍝ metricType: 0=Counter, 1=Gauge, 2=Histogram
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (name val mtype tpl attrs)←5↑args,(⍴,args)↓'' 0 0 '' ⍬
      'pp_otel_metric' ⎕NA 'dyalogotel|pp_otel_metric I4 <PP <PP I4 <PP <PP'
      pp_otel_metric pipeline name val mtype tpl attrs
    ∇

    ∇ TplCreate args;name;attrs;pipeline
    ⍝ Create a named template.
    ⍝ args: name attrs
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (name attrs)←2↑args
      'pp_otel_tpl_create' ⎕NA 'dyalogotel|pp_otel_tpl_create I4 <PP <PP'
      pp_otel_tpl_create pipeline name attrs
    ∇

    ∇ TplDerive args;child;parent;extras;pipeline
    ⍝ Derive a child template from a parent.
    ⍝ args: childName parentName extraAttrs
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      (child parent extras)←3↑args
      'pp_otel_tpl_derive' ⎕NA 'dyalogotel|pp_otel_tpl_derive I4 <PP <PP <PP'
      pp_otel_tpl_derive pipeline child parent extras
    ∇

    ∇ TplDelete args;name;pipeline
    ⍝ Delete a template (cascades to children).
    ⍝ args: name
      :Access Public Shared
      pipeline←0
      :If 2=⎕NC'⍺' ⋄ pipeline←⍺ ⋄ :EndIf
      name←⊃args
      'pp_otel_tpl_delete' ⎕NA 'dyalogotel|pp_otel_tpl_delete I4 <PP'
      pp_otel_tpl_delete pipeline name
    ∇

    ∇ r←Status pipeline
    ⍝ Get pipeline status: 0=healthy, 1=degraded, 2=down
      :Access Public Shared
      'pp_otel_status' ⎕NA 'dyalogotel|pp_otel_status I4 >PP'
      r←pp_otel_status pipeline
    ∇

    ∇ r←Stats pipeline
    ⍝ Get 3×3 matrix: (log span metric) × (enqueued exported dropped)
      :Access Public Shared
      'pp_otel_stats' ⎕NA 'dyalogotel|pp_otel_stats I4 >PP'
      r←3 3⍴pp_otel_stats pipeline
    ∇

    ∇ Flush pipeline
    ⍝ Block until channels are drained.
      :Access Public Shared
      'pp_otel_flush' ⎕NA 'dyalogotel|pp_otel_flush I4'
      pp_otel_flush pipeline
    ∇

    ∇ Shutdown pipeline
    ⍝ Flush and tear down a pipeline (0 = singleton).
      :Access Public Shared
      'pp_otel_shutdown' ⎕NA 'dyalogotel|pp_otel_shutdown I4'
      pp_otel_shutdown pipeline
    ∇

    ∇ r←Version
    ⍝ Get library version string.
      :Access Public Shared
      'pp_otel_version' ⎕NA 'dyalogotel|pp_otel_version >PP'
      r←pp_otel_version
    ∇

:EndNamespace
