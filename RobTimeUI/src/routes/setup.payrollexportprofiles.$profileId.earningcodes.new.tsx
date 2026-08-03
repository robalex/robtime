import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { PayrollEarningCodeMappingForm } from '@/features/payrollEarningCodeMappings/PayrollEarningCodeMappingForm'
import { DEFAULT_PAYROLL_EARNING_CODE_MAPPING_FORM_VALUES } from '@/features/payrollEarningCodeMappings/formSchema'
import { useCreatePayrollEarningCodeMapping } from '@/features/payrollEarningCodeMappings/queries'

export const Route = createFileRoute('/setup/payrollexportprofiles/$profileId/earningcodes/new')({
  component: NewEarningCodeMapping,
})

function NewEarningCodeMapping() {
  const { profileId } = Route.useParams()
  const id = Number(profileId)
  const navigate = useNavigate()
  const createMapping = useCreatePayrollEarningCodeMapping(id)

  const backToProfile = () =>
    navigate({
      to: '/setup/payrollexportprofiles/$profileId',
      params: { profileId },
      search: { tab: 'earningcodes' },
    })

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New earning-code mapping</h1>
      <PayrollEarningCodeMappingForm
        defaultValues={DEFAULT_PAYROLL_EARNING_CODE_MAPPING_FORM_VALUES}
        submitLabel="Create mapping"
        onCancel={() => void backToProfile()}
        onSubmit={async (values) => {
          await createMapping.mutateAsync({
            lineType: values.lineType,
            lineCode: values.lineCode,
            earningCode: values.earningCode,
            valueBasis: values.valueBasis,
            description: values.description || undefined,
          })
          await backToProfile()
        }}
      />
    </div>
  )
}
