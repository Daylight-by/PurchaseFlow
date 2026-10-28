# PurchaseFlow 源码交付包

这是 PurchaseFlow 采购询价与审核工作台的源码包，包含：

- .NET 10、ABP Framework 10.6.2、ASP.NET Core Razor Pages、EF Core、SQLite 源码
- Domain、Application.Contracts、Application、EntityFrameworkCore、Web、DbMigrator
- 自动化测试、EF Core 迁移、README、业务规则和学习资料

本包已排除本机 `.tools`、数据库、证书、日志、构建缓存和本机演示账号配置。

接手时请先阅读根目录 `README.md`，再按照本机环境重新配置连接字符串、OpenIddict 客户端密钥、演示账号和 HTTPS 证书。`appsettings.json` 中的敏感示例值已经替换为占位符。

这是学习和面试演示原型，不是已经完成生产部署的正式采购系统。规则和示例数据均为模拟数据。
