{{- define "openvp.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "openvp.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "openvp.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "openvp.labels" -}}
helm.sh/chart: {{ include "openvp.chart" . }}
{{ include "openvp.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "openvp.selectorLabels" -}}
app.kubernetes.io/name: {{ include "openvp.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/* The Secret that holds the access key: yours, or the one this chart makes. */}}
{{- define "openvp.secretName" -}}
{{- default (include "openvp.fullname" .) .Values.existingSecret }}
{{- end }}

{{- define "openvp.secretKey" -}}
{{- if .Values.existingSecret }}{{ .Values.existingSecretKey }}{{ else }}api-key{{ end }}
{{- end }}

{{- define "openvp.claimName" -}}
{{- default (include "openvp.fullname" .) .Values.persistence.existingClaim }}
{{- end }}
