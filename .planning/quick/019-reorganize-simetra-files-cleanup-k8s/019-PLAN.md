---
phase: quick-019
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - reference/simetra/ (moved from src/Simetra/)
  - reference/Simetra.sln (moved from Simetra.sln)
  - deploy/k8s/deployment.yaml (deleted)
  - deploy/k8s/configmap.yaml (deleted)
  - deploy/k8s/service.yaml (deleted)
  - deploy/k8s/production/deployment.yaml
  - deploy/k8s/production/service.yaml
  - deploy/k8s/production/service-nodeports.yaml
autonomous: true

must_haves:
  truths:
    - "src/Simetra/ no longer exists; reference code lives in reference/simetra/"
    - "Simetra.sln is in reference/ with corrected project path"
    - "deploy/k8s/ has no duplicate deployment.yaml, configmap.yaml, or service.yaml"
    - "Production K8s manifests use snmp-collector naming (not simetra) for app labels and container/image"
    - "Namespace remains simetra throughout all manifests"
  artifacts:
    - path: "reference/simetra/Simetra.csproj"
      provides: "Moved reference project"
    - path: "reference/Simetra.sln"
      provides: "Reference solution with corrected path"
    - path: "deploy/k8s/production/deployment.yaml"
      provides: "Production deployment with snmp-collector naming"
      contains: "name: snmp-collector"
  key_links: []
---

<objective>
Reorganize the repo to clearly separate the Simetra reference project from the active snmp-collector project, remove duplicate/wrong top-level K8s manifests, and rename simetra to snmp-collector in production K8s YAMLs.

Purpose: Eliminate confusion between reference code and active project, remove stale manifests that could cause deployment errors.
Output: Clean repo structure with reference/ directory and consistent K8s naming.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Move Simetra source to reference/ directory</name>
  <files>
    reference/simetra/ (new, moved from src/Simetra/)
    reference/Simetra.sln (new, moved from Simetra.sln)
  </files>
  <action>
    1. Create reference/ directory: `mkdir -p reference`
    2. Move the Simetra project: `git mv src/Simetra/ reference/simetra/`
    3. Move the solution file: `git mv Simetra.sln reference/Simetra.sln`
    4. Update reference/Simetra.sln project path from `src\Simetra\Simetra.csproj` to `simetra\Simetra.csproj` (line 8)
       - Also remove the "src" solution folder entry (lines 6-7) and its NestedProjects entry (lines 24-25) since the project is no longer nested under src/
       - Keep the project GUID and configuration unchanged

    DO NOT touch src/SnmpCollector/ or tests/ -- those are the active project.
  </action>
  <verify>
    - `test -d reference/simetra && echo OK` prints OK
    - `test -f reference/Simetra.sln && echo OK` prints OK
    - `test ! -d src/Simetra && echo OK` prints OK (old location gone)
    - `grep 'simetra\\Simetra.csproj' reference/Simetra.sln` shows updated path
  </verify>
  <done>src/Simetra/ no longer exists, reference/simetra/ contains all Simetra source, Simetra.sln has correct relative path to csproj</done>
</task>

<task type="auto">
  <name>Task 2: Delete duplicate top-level K8s manifests</name>
  <files>
    deploy/k8s/deployment.yaml (deleted)
    deploy/k8s/configmap.yaml (deleted)
    deploy/k8s/service.yaml (deleted)
  </files>
  <action>
    Delete these three files that are duplicates or wrong-named versions of manifests that already exist correctly elsewhere:

    1. `git rm deploy/k8s/deployment.yaml` -- wrong "simetra" deployment; correct one is deploy/k8s/snmp-collector/deployment.yaml
    2. `git rm deploy/k8s/configmap.yaml` -- simetra-named configmaps; snmp-collector uses simetra-config/simetra-oidmaps/simetra-devices
    3. `git rm deploy/k8s/service.yaml` -- duplicate; snmp-collector has its own service definition

    DO NOT delete namespace.yaml, serviceaccount.yaml, or rbac.yaml -- those are shared infrastructure.
  </action>
  <verify>
    - `test ! -f deploy/k8s/deployment.yaml && echo OK` prints OK
    - `test ! -f deploy/k8s/configmap.yaml && echo OK` prints OK
    - `test ! -f deploy/k8s/service.yaml && echo OK` prints OK
    - `ls deploy/k8s/*.yaml` still shows namespace.yaml, rbac.yaml, serviceaccount.yaml
  </verify>
  <done>Three duplicate/wrong top-level K8s manifests are removed; shared infrastructure manifests remain</done>
</task>

<task type="auto">
  <name>Task 3: Rename simetra to snmp-collector in production K8s YAMLs</name>
  <files>
    deploy/k8s/production/deployment.yaml
    deploy/k8s/production/service.yaml
    deploy/k8s/production/service-nodeports.yaml
  </files>
  <action>
    Update production manifests to use snmp-collector as the app name. The namespace stays as `simetra` (product family). ConfigMap names stay as-is. ServiceAccount stays as-is.

    **deploy/k8s/production/deployment.yaml:**
    - metadata.name: simetra -> snmp-collector
    - metadata.labels.app: simetra -> snmp-collector
    - spec.selector.matchLabels.app: simetra -> snmp-collector
    - spec.template.metadata.labels.app: simetra -> snmp-collector
    - containers[0].name: simetra -> snmp-collector
    - containers[0].image: simetra:local -> snmp-collector:local
    - Keep namespace: simetra, serviceAccountName: simetra-sa, configmap names all unchanged

    **deploy/k8s/production/service.yaml:**
    - metadata.name: simetra -> snmp-collector
    - metadata.labels.app: simetra -> snmp-collector
    - spec.selector.app: simetra -> snmp-collector
    - Keep namespace: simetra

    **deploy/k8s/production/service-nodeports.yaml:**
    - First service (SNMP trap NodePort) only:
      - metadata.name: simetra-snmp-traps -> snmp-collector-snmp-traps
      - metadata.labels.app: simetra -> snmp-collector
      - spec.selector.app: simetra -> snmp-collector
      - Update comments referencing "simetra pod" to "snmp-collector pod"
    - DO NOT change prometheus-nodeport, elasticsearch-nodeport, or grafana-nodeport services -- those have their own app labels

    IMPORTANT: Do NOT do a blind find-replace. The word "simetra" appears in namespace, serviceaccount, and configmap references that must NOT change. Edit surgically.
  </action>
  <verify>
    - `grep 'name: snmp-collector' deploy/k8s/production/deployment.yaml` shows the deployment name
    - `grep 'image: snmp-collector:local' deploy/k8s/production/deployment.yaml` shows correct image
    - `grep 'namespace: simetra' deploy/k8s/production/deployment.yaml` confirms namespace unchanged
    - `grep 'simetra-config' deploy/k8s/production/deployment.yaml` confirms configmap name unchanged
    - `grep 'name: snmp-collector' deploy/k8s/production/service.yaml` shows service name
    - `grep 'snmp-collector-snmp-traps' deploy/k8s/production/service-nodeports.yaml` shows nodeport name
    - `grep 'app: prometheus' deploy/k8s/production/service-nodeports.yaml` confirms other services untouched
  </verify>
  <done>Production deployment, service, and nodeport manifests use snmp-collector as app name; namespace, configmaps, and serviceaccount remain simetra-prefixed</done>
</task>

</tasks>

<verification>
1. `git status` shows all moves, deletes, and modifications staged correctly
2. `test -d reference/simetra` -- reference directory exists
3. `test ! -d src/Simetra` -- old location removed
4. `ls deploy/k8s/*.yaml | wc -l` equals 3 (namespace, rbac, serviceaccount)
5. `grep -r 'app: simetra' deploy/k8s/production/deployment.yaml` returns nothing (renamed to snmp-collector)
6. `grep 'namespace: simetra' deploy/k8s/production/deployment.yaml` returns match (namespace preserved)
7. `dotnet build src/SnmpCollector/SnmpCollector.sln` still succeeds (active project untouched)
</verification>

<success_criteria>
- Simetra reference project lives in reference/simetra/ with working .sln
- No duplicate K8s manifests in deploy/k8s/ root
- Production K8s YAMLs consistently use snmp-collector as app identity
- Namespace remains simetra everywhere
- Active snmp-collector project completely untouched
</success_criteria>

<output>
After completion, create `.planning/quick/019-reorganize-simetra-files-cleanup-k8s/019-SUMMARY.md`
</output>
