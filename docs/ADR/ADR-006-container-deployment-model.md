# ADR-006: Container Deployment Model

## Status
Proposed

## Context
We are decomposing the monolith into microservices and need to decide on a deployment model. The requirements are:

1. **Container-based deployment** (as per project constraints)
2. **Independent service deployment** without coordination
3. **Scalability** (horizontal scaling for high-demand services)
4. **High availability** (no single points of failure)
5. **Cost-effective** for development and production environments
6. **Operationally manageable** by a small team

## Decision

We will use **Docker containers orchestrated by Kubernetes** for all services.

### Container Technology: Docker

- All services packaged as Docker images
- Multi-stage Dockerfiles for optimal image size
- Base image: `mcr.microsoft.com/dotnet/aspnet:8.0-alpine` (production)
- Build image: `mcr.microsoft.com/dotnet/sdk:8.0` (build stage)
- Images tagged with semantic versioning: `retail/product-service:1.2.3`
- Images stored in Azure Container Registry (ACR) or Docker Hub

### Orchestration: Kubernetes

- **Development**: Local Kubernetes (Docker Desktop or Minikube)
- **Staging**: Azure Kubernetes Service (AKS) - single-node cluster
- **Production**: Azure Kubernetes Service (AKS) - multi-node cluster (3+ nodes)

### Deployment Pattern

- **Deployment**: One Kubernetes Deployment per service
- **Replicas**: 3 replicas per service (for HA)
- **Service**: Kubernetes Service (ClusterIP) for internal communication
- **Ingress**: NGINX Ingress Controller for external access
- **ConfigMap**: Environment-specific configuration
- **Secret**: Sensitive data (DB credentials, API keys)
- **HorizontalPodAutoscaler**: Auto-scaling based on CPU (70% threshold)

### Example Deployment Manifest (Product Service)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: product-service
  namespace: retail-prod
spec:
  replicas: 3
  selector:
    matchLabels:
      app: product-service
  template:
    metadata:
      labels:
        app: product-service
    spec:
      containers:
      - name: product-service
        image: retail/product-service:1.2.3
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: product-db-secret
              key: connection-string
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
        resources:
          requests:
            memory: "256Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

## Rationale

### Why Docker?

1. **Industry Standard**: Most widely used containerization technology
2. **Ecosystem**: Rich tooling (Docker Compose, Docker Desktop)
3. **Portability**: Same image runs on dev laptop and production cluster
4. **Isolation**: Each service has its own dependencies
5. **Fast Startup**: Containers start in seconds
6. **Team Familiarity**: Team has Docker experience

**Alternative considered**: Podman → Rejected due to less mature ecosystem and team unfamiliarity.

### Why Kubernetes?

1. **Industry Standard**: De facto orchestrator for containers
2. **Cloud-Native**: First-class support in Azure, AWS, GCP
3. **Auto-Scaling**: HorizontalPodAutoscaler handles load automatically
4. **Self-Healing**: Unhealthy pods automatically restarted
5. **Service Discovery**: Built-in DNS for service-to-service communication
6. **Rolling Updates**: Zero-downtime deployments
7. **Strong Community**: Huge ecosystem of tools and patterns

**Alternative considered**: Docker Swarm → Rejected due to declining industry adoption and fewer features than Kubernetes.

**Alternative considered**: Azure Container Apps → Deferred for future consideration. While simpler than Kubernetes, we want full control and portability across clouds.

### Why Azure Kubernetes Service (AKS)?

1. **Managed Control Plane**: Microsoft manages master nodes (free)
2. **Azure Integration**: Native integration with ACR, Key Vault, Application Insights
3. **Automatic Upgrades**: Kubernetes version upgrades managed by Azure
4. **Cost**: Pay only for worker nodes, control plane is free
5. **Reliability**: 99.95% SLA with availability zones

**Alternative considered**: Self-hosted Kubernetes → Rejected due to operational complexity and lack of dedicated ops team.

**Alternative considered**: AWS EKS or GCP GKE → Rejected because team/organization already uses Azure.

### Why 3 Replicas per Service?

1. **High Availability**: Survives single-node failure
2. **Load Balancing**: Distribute traffic across pods
3. **Zero-Downtime Deployments**: Rolling updates with maxUnavailable=1

**Alternative considered**: 1 replica (dev) → Acceptable for development, not production.

**Alternative considered**: 5+ replicas → Overkill for initial deployment, can scale up based on load.

## Consequences

### Positive

- **Consistent Environments**: Dev, staging, prod all run containers
- **Independent Scaling**: Scale Product Service to 10 replicas, Cart Service to 3
- **Fast Rollbacks**: `kubectl rollout undo deployment/product-service`
- **Self-Healing**: Failed pods automatically restarted
- **Resource Efficiency**: Pack multiple containers per node
- **Declarative Config**: GitOps approach with manifests in Git

### Negative

- **Learning Curve**: Team must learn Kubernetes (kubectl, YAML, concepts)
- **Complexity**: More moving parts than monolith on VM
- **Debugging**: Logs are distributed across pods (need centralized logging)
- **Cost**: AKS worker nodes incur compute costs (even if underutilized)
- **YAML Verbosity**: Kubernetes manifests are verbose (mitigated by Helm)

### Operational Impact

**New Skills Required**:
- Docker: Building images, multi-stage builds, optimization
- Kubernetes: Deployments, Services, ConfigMaps, Secrets, kubectl
- Helm: Templating and packaging manifests (optional, future)
- Monitoring: Prometheus, Grafana, distributed tracing

**Tools Required**:
- kubectl CLI
- Docker Desktop (dev)
- Lens or K9s (Kubernetes UI)
- Azure CLI (for AKS management)

### Cost Analysis

**Development**:
- Local Kubernetes: Free (Docker Desktop)
- AKS Cluster: 1 node (D2s_v3) → ~$70/month
- Azure SQL: Basic tier → $5/month per DB
- Redis: Basic → $20/month
- **Total**: ~$150/month

**Production**:
- AKS Cluster: 3 nodes (D4s_v3) → ~$420/month
- Azure SQL: Standard S2 → $120/month per DB (6 DBs) → $720/month
- Redis: Standard C1 → $75/month
- Ingress Controller: Free (NGINX) or ~$125/month (Application Gateway)
- **Total**: ~$1,340/month (without Application Gateway)

**Optimization Strategies**:
- Use spot instances for non-production (30-90% savings)
- Auto-scale down during off-hours
- Use reserved instances for production (30-50% savings)
- Single SQL server with multiple databases (shared infrastructure)

## Implementation Plan

### Phase 0: Infrastructure Setup

1. **Local Development**:
   - Install Docker Desktop with Kubernetes enabled
   - Test monolith deployment locally
   - Verify kubectl and docker commands work

2. **Azure AKS Setup**:
   - Create AKS cluster: `az aks create --resource-group retail-rg --name retail-aks --node-count 3 --enable-managed-identity`
   - Create Azure Container Registry: `az acr create --resource-group retail-rg --name retailacr --sku Standard`
   - Connect AKS to ACR: `az aks update --name retail-aks --resource-group retail-rg --attach-acr retailacr`
   - Install NGINX Ingress Controller: `helm install ingress-nginx ingress-nginx/ingress-nginx`

3. **CI/CD Pipeline**:
   - GitHub Actions workflow to build Docker images
   - Push images to ACR
   - Update Kubernetes manifests with new image tag
   - Apply manifests to AKS

### Per-Service Deployment Steps

1. **Create Dockerfile**:
   ```dockerfile
   # Build stage
   FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
   WORKDIR /src
   COPY ["ProductService.csproj", "./"]
   RUN dotnet restore
   COPY . .
   RUN dotnet publish -c Release -o /app/publish

   # Runtime stage
   FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
   WORKDIR /app
   COPY --from=build /app/publish .
   EXPOSE 8080
   ENTRYPOINT ["dotnet", "ProductService.dll"]
   ```

2. **Create Kubernetes Manifests**:
   - `deployment.yaml` (replicas, image, resources, health checks)
   - `service.yaml` (ClusterIP, port 80)
   - `configmap.yaml` (non-sensitive config)
   - `secret.yaml` (DB connection string)
   - `hpa.yaml` (auto-scaling rules)

3. **Deploy to Kubernetes**:
   ```bash
   kubectl apply -f k8s/product-service/
   kubectl rollout status deployment/product-service
   kubectl get pods -l app=product-service
   ```

4. **Verify Health**:
   ```bash
   kubectl port-forward svc/product-service 8080:80
   curl http://localhost:8080/health
   ```

## Health Checks

All services MUST implement `/health` endpoint:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddRedis(builder.Configuration["Redis:ConnectionString"]);

app.MapHealthChecks("/health");
```

Kubernetes uses health checks for:
- **Liveness Probe**: Restart pod if unhealthy
- **Readiness Probe**: Remove from load balancer if not ready

## Resource Limits

All pods MUST define resource requests and limits:

```yaml
resources:
  requests:
    memory: "256Mi"  # Guaranteed minimum
    cpu: "100m"      # 0.1 CPU core
  limits:
    memory: "512Mi"  # Hard limit (OOMKilled if exceeded)
    cpu: "500m"      # Throttled if exceeded
```

**Guidelines**:
- Start with conservative limits
- Monitor actual usage in production
- Adjust based on metrics (CPU, memory, request rate)

## Auto-Scaling

Configure HorizontalPodAutoscaler for each service:

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: product-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: product-service
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
```

**Scaling Rules**:
- Scale up if CPU > 70% for 2 minutes
- Scale down if CPU < 40% for 5 minutes
- Min replicas: 3 (HA)
- Max replicas: 10 (cost control)

## Deployment Strategies

### Rolling Update (Default)

- **MaxUnavailable**: 1 (max pods down during update)
- **MaxSurge**: 1 (max extra pods during update)
- **Zero Downtime**: Old pods remain until new pods are ready
- **Rollback**: `kubectl rollout undo deployment/product-service`

### Blue-Green (Optional, Future)

- Deploy new version to separate namespace
- Test in isolation
- Switch traffic via Ingress routing
- Instant rollback by switching back

### Canary (Optional, Future)

- Deploy new version to small % of pods (e.g., 10%)
- Monitor metrics (error rate, response time)
- Gradually increase % (25% → 50% → 100%)
- Automatic rollback if metrics degrade

**Initial Decision**: Start with Rolling Update, add Blue-Green/Canary in Phase 3+ if needed.

## Security Considerations

### Container Security

- Run as non-root user
- Read-only root filesystem (where possible)
- Drop all capabilities, add only needed ones
- Scan images for vulnerabilities (Trivy, Azure Defender)

### Network Security

- Kubernetes Network Policies to restrict pod-to-pod traffic
- Only allow necessary service-to-service communication
- No public internet access from pods (except Payment Service)

### Secrets Management

- Use Kubernetes Secrets (encrypted at rest in etcd)
- Or Azure Key Vault integration (recommended for production)
- Never commit secrets to Git
- Rotate secrets regularly

## Monitoring

### Key Metrics per Service

- **Pod count**: Desired vs actual
- **Pod restarts**: High restart count indicates crashes
- **CPU usage**: % of requested and limit
- **Memory usage**: % of requested and limit
- **Request rate**: Requests/second
- **Error rate**: 5xx errors/total requests
- **Response time**: p50, p95, p99

### Dashboards

- Cluster overview (node health, pod count)
- Per-service dashboard (metrics above)
- Resource utilization (cost optimization)

### Alerts

- Pod crash looping → Page on-call
- High memory usage (> 80%) → Notify team
- Auto-scaling maxed out → Consider increasing limits

## Alternatives Considered

### Serverless (Azure Functions)

**Pros**: 
- Zero infrastructure management
- Pay per execution (cost savings for low traffic)
- Auto-scaling built-in

**Cons**: 
- Cold start latency
- Vendor lock-in (Azure-specific)
- Limited runtime environment
- Difficult to run locally

**Decision**: Rejected for core services, may use for background jobs/triggers.

### Virtual Machines

**Pros**: 
- Team familiar with VMs
- Simple deployment (scp + systemd)

**Cons**: 
- Manual scaling (slow)
- No self-healing
- Higher operational overhead
- Harder to achieve HA

**Decision**: Rejected, doesn't meet container-based deployment requirement.

## Related ADRs

- **ADR-005**: Service Decomposition Strategy (what services to deploy)
- **ADR-007**: API Gateway Pattern (how external traffic reaches services)

## Date
2026-01-14

## Participants
- DevOps Team
- Architecture Team
- Development Team

## Review
This ADR should be reviewed after Phase 0 completion to validate Kubernetes setup and adjust based on team feedback.
