import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { PayrollExportProfileForm } from '@/features/payrollExportProfiles/PayrollExportProfileForm'
import { DEFAULT_PAYROLL_EXPORT_PROFILE_FORM_VALUES } from '@/features/payrollExportProfiles/formSchema'
import { useCreatePayrollExportProfile } from '@/features/payrollExportProfiles/queries'
import { RequiresClient } from '@/components/RequiresClient'

export const Route = createFileRoute('/setup/payrollexportprofiles/new')({
  component: NewPayrollExportProfile,
})

function NewPayrollExportProfile() {
  return <RequiresClient>{(clientId) => <NewPayrollExportProfileForm clientId={clientId} />}</RequiresClient>
}

function NewPayrollExportProfileForm({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const createProfile = useCreatePayrollExportProfile()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New payroll export profile</h1>
      <PayrollExportProfileForm
        defaultValues={DEFAULT_PAYROLL_EXPORT_PROFILE_FORM_VALUES}
        submitLabel="Create profile"
        onCancel={() => void navigate({ to: '/setup/payrollexportprofiles' })}
        onSubmit={async (values) => {
          const created = await createProfile.mutateAsync({
            clientId,
            name: values.name,
            provider: values.provider,
            grouping: values.grouping,
            roundingPolicy: values.roundingPolicy,
            adjustmentEarningCode: values.adjustmentEarningCode || undefined,
            amountScale: values.amountScale,
            hoursScale: values.hoursScale,
          })
          await navigate({
            to: '/setup/payrollexportprofiles/$profileId',
            params: { profileId: String(created!.id) },
          })
        }}
      />
    </div>
  )
}
