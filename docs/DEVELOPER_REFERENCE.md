# خريطة ملفات المطور

| ملف | المسؤولية |
|---|---|
| `src/Wahib.Domain/Amounts.cs` | تحويل الأموال/الكميات الصحيحة والتحقق من التوازن ومتوسط التكلفة |
| `src/Wahib.Domain/DocumentRules.cs` | أنماط المستندات والأدوار الأساسية |
| `src/Wahib.Application/Contracts.cs` | Data Contracts وواجهات الاستخدام |
| `src/Wahib.Infrastructure/Sql.cs` | تنفيذ استعلامات SQLite المعلّمة بالمعاملات |
| `src/Wahib.Infrastructure/Schema.cs` | Schema v1 وChecks وSeed حسابات النظام |
| `src/Wahib.Infrastructure/CompanyStore.cs` | إدارة الشركات المحلية وملفاتها المنفصلة |
| `src/Wahib.Infrastructure/Passwords.cs` | PBKDF2 مع Salt وتحقق ثابت الزمن |
| `src/Wahib.Infrastructure/SqliteErpGateway.Core.cs` | المصادقة/العناصر/الأطراف/التسلسل/سجل التدقيق |
| `src/Wahib.Infrastructure/SqliteErpGateway.Documents.cs` | مسودات، ترحيل، بيع وشراء ومرتجعات |
| `src/Wahib.Infrastructure/SqliteErpGateway.Operations.cs` | السداد، الجرد، القيود اليدوية |
| `src/Wahib.Infrastructure/SqliteErpGateway.Reports.cs` | تقرير الأستاذ، الميزان، الدخل، المركز المالي والتصالح |
| `src/Wahib.Infrastructure/SqliteErpGateway.Backup.cs` | نسخ API والاستعادة والـSHA256 |
| `src/Wahib.Desktop/MainWindow.xaml(.cs)` | واجهة التنقل الرئيسية |
| `src/Wahib.Desktop/Views/InvoiceDialog.cs` | جمع إدخال بنود متعددة |
| `src/Wahib.Desktop/Views/InvoicePrinter.cs` | طباعة A4 وتصدير CSV |
| `src/Wahib.Desktop/Views/ReportPanel.cs` | تقارير الواجهة |
| `src/Wahib.Desktop/Views/AdminPanel.cs` | المستخدمون والنسخ والفترات |
| `tests/Wahib.Tests/Program.cs` | اختبار مستقل Console مع SQLite حقيقي |

## قواعد للتوسعة

- اضف تعديل Schema باستخدام **migration محدد** بعد النسخ والاختبار. لا تعدّل `Schema.cs` في برنامج مستخدم بقاعدة موجودة دون سياسة Upgrade.
- حافظ على `JournalEntries.source_id UNIQUE`، ولا تغيّر قيود مرّحلة مباشرة.
- اجعل كل posted Document + StockMovements + JournalLines في SQLite transaction واحدة.
- أبق كل الحركات التاريخية immutable، وعالج الإلغاء باستخدام المستندات العكسية/المرتجعات.
- لا تغيّر عملة الشركة بعد أول ترحيل. لا تستخدم Float/Double في محاسبة النقد.
- حافظ على `Test Evidence` وربط FR/NFR بالأدلة قبل تعديل حالة أي متطلب إلى Verified.
